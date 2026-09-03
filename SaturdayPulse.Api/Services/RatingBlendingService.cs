using SaturdayPulse.Configuration;
using SaturdayPulse.Models;
using Microsoft.Extensions.Options;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// PROMOTED — implements the data-volume-weighted ("K=4 inertia") blending
    /// formula. Despite the "EXPERIMENTAL"/"parallel comparison path" framing this
    /// header originally had, this IS the live production rating source —
    /// GamePredictionService.GetRatingsForWeekAsync delegates to
    /// ExperimentalInertiaRatingService, which calls this class directly. Confirmed
    /// 2026-09-02; header corrected same day after nearly misleading a debugging
    /// session into treating this as dead code. RatingComparisonService still uses
    /// it too, for backtesting.
    ///
    /// currentSeasonWeight = gamesPlayed / (K + gamesPlayed)
    ///
    /// DEV PLACEHOLDER: K = 4.0 is a reasonable starting default, NOT a validated
    /// "industry standard" — see conversation history. Checked against a cited source
    /// (a Towards Data Science piece on basketball/golf rating systems) that turned out
    /// not to support the specific K=4 / ESPN FPI / SP+ claims attributed to it. Tune
    /// via the RatingComparisonService backtest output before treating this as final.
    /// STILL a placeholder as of the 2026-08-20 anchor-blend fix below — that fix only
    /// addressed ComputeSeededAnchorUnit's own internal weighting (TrendRating/SeedRating
    /// vs ZRoster), not BlendUnit's K, which is a separate open question.
    ///
    /// UPDATED 2026-09-03 — ComputeSeededAnchorUnit's seedUnit now carries more
    /// dispersion than before at the same call site (RollingAverageService widened
    /// SeedRating's own clamp specifically; see its SeedClampSigma remarks) — no
    /// code change needed here, since this method already just reads
    /// record.SeedRating directly. Fixes the compressed early-season predictions
    /// this blend was producing (most contenders' SeedRating pooling near the same
    /// ceiling under the old +-2 std dev clamp).
    ///
    /// NEW FILE — part of the K=4 inertia-blending experimental comparison path.
    /// </summary>
    public class RatingBlendingService
    {
        private readonly MetricsConfiguration _config;

        public RatingBlendingService(IOptions<MetricsConfiguration> config)
            => _config = config.Value;

        /// <summary>
        /// SeededAnchor = SeedRating (Trend + Pedigree blend, already unit-scale)
        /// blended with ZRoster (mapped onto the same [0,1] scale). Replaces the
        /// old week-0-snapshot-as-anchor approach for the experimental path only.
        /// SeedRating itself is untouched — this is a derived value computed at
        /// blend time, not a write to any TeamRecord column.
        ///
        /// UPDATED 2026-08-20 — two changes together, both from the same real-data
        /// calibration (AnchorBlendCalculator), replacing what were both dev
        /// placeholders never validated against actual outcomes:
        ///
        ///   1. TrendRating → SeedRating. AnchorBlendCalculator's rolling-window
        ///      calibration is deliberately windowed to the trailing 3 seasons
        ///      (see AnchorBlendCoefficient remarks on why — transfer-portal-era
        ///      drift in ZRoster's importance). TrendRating is itself a 5-year
        ///      window, which would outlive a 3-year calibration and quietly pull
        ///      in data from outside the period being fit. SeedRating also fit
        ///      marginally but consistently better in backtesting. Using
        ///      TrendRating here while calibrating against SeedRating would be
        ///      internally inconsistent — they need to match.
        ///
        ///   2. The old (trendUnit + zRosterUnit) / 2.0 straight average, and the
        ///      ZRoster * 0.05-then-map-with-stdDev=0.25 compression feeding it,
        ///      are both gone. Real games say TrendRating/SeedRating-equivalent
        ///      signal outweighs ZRoster's independent contribution by roughly
        ///      3-4x (standardized regression coefficients, not a guess) — a 50/50
        ///      split was overweighting ZRoster relative to what it's actually
        ///      worth, regardless of how compressed the input was. coefficient's
        ///      ZRosterWeight/RatingWeight (sum to 1.0) replace the fixed 50/50;
        ///      its ZRosterMean/ZRosterStdDev (real distribution over the
        ///      calibration window) replace the arbitrary mean=0/stdDev=0.25.
        ///
        /// coefficient is null when AnchorBlendCalculator hasn't run yet for this
        /// season (e.g. the first 3 seasons of a from-scratch historical backfill,
        /// mirroring TierDiscountCoefficient's identical gap) — falls back to the
        /// exact prior behavior (50/50, mean=0/stdDev=0.25) rather than breaking,
        /// same "missing coefficient = safe default, not an error" convention
        /// BuildProjection already uses for a null TierDiscountCoefficient.
        /// </summary>
        public double ComputeSeededAnchorUnit(TeamRecord record, AnchorBlendCoefficient? coefficient)
        {
            double seedUnit = record.SeedRating.HasValue ? (double)record.SeedRating.Value : 0.5;

            if (!record.ZRoster.HasValue)
                return seedUnit;

            double zRosterMean = coefficient != null ? (double)coefficient.ZRosterMean : 0.0;
            double zRosterStdDev = coefficient != null ? (double)coefficient.ZRosterStdDev : 0.25;
            double zRosterWeight = coefficient != null ? (double)coefficient.ZRosterWeight : 0.5;
            double ratingWeight = coefficient != null ? (double)coefficient.RatingWeight : 0.5;

            // ZRoster mapped onto [0,1] using this window's REAL mean/stddev
            // (from the coefficient row) rather than an arbitrary compression —
            // no more "* 0.05" step; ToUnitScale does the real standardization now.
            double zRosterUnit = RatingScaling.ToUnitScale(
                (double)record.ZRoster.Value, mean: zRosterMean, stdDev: zRosterStdDev);

            return (seedUnit * ratingWeight) + (zRosterUnit * zRosterWeight);
        }

        /// <summary>
        /// Blends the seeded anchor with this week's live cross-sectional rating,
        /// weighted by games actually played. Smooth across all weeks — no hard cutover.
        /// </summary>
        public double BlendUnit(double anchorUnit, double liveUnit, int gamesPlayed)
        {
            double k = _config.InertiaConstant; // default 4.0 — see class remarks
            double currentSeasonWeight = gamesPlayed / (k + gamesPlayed);
            double anchorWeight = 1.0 - currentSeasonWeight;

            return (anchorUnit * anchorWeight) + (liveUnit * currentSeasonWeight);
        }
    }
}
