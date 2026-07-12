using SaturdayPulse.Configuration;
using SaturdayPulse.Models;
using Microsoft.Extensions.Options;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// EXPERIMENTAL — implements the data-volume-weighted ("K=4 inertia") blending
    /// formula as a parallel comparison path against the production week-6 snapshot
    /// cliff in GamePredictionService.GetRatingsForWeekAsync. Not wired into any
    /// production prediction path; used only by ExperimentalInertiaRatingService and
    /// RatingComparisonService.
    ///
    /// currentSeasonWeight = gamesPlayed / (K + gamesPlayed)
    ///
    /// DEV PLACEHOLDER: K = 4.0 is a reasonable starting default, NOT a validated
    /// "industry standard" — see conversation history. Checked against a cited source
    /// (a Towards Data Science piece on basketball/golf rating systems) that turned out
    /// not to support the specific K=4 / ESPN FPI / SP+ claims attributed to it. Tune
    /// via the RatingComparisonService backtest output before treating this as final.
    ///
    /// NEW FILE — part of the K=4 inertia-blending experimental comparison path.
    /// </summary>
    public class RatingBlendingService
    {
        private readonly MetricsConfiguration _config;

        public RatingBlendingService(IOptions<MetricsConfiguration> config)
            => _config = config.Value;

        /// <summary>
        /// SeededAnchor = TrendRating (5-yr, already unit-scale) blended with ZRoster
        /// (mapped onto the same [0,1] scale). Replaces the old week-0-snapshot-as-anchor
        /// approach for the experimental path only. TrendRating itself is untouched —
        /// pure historical, no ZRoster — this is a derived value computed at blend time,
        /// not a write to the TrendRating column. Per Charlie's confirmation, Trend (not
        /// a new column) is reused directly as the 5-year anchor source.
        /// </summary>
        public double ComputeSeededAnchorUnit(TeamRecord record, decimal zRosterScalingConstant)
        {
            double trendUnit = record.TrendRating.HasValue ? (double)record.TrendRating.Value : 0.5;

            if (!record.ZRoster.HasValue)
                return trendUnit;

            // ZRoster is already a national z-score by construction — no additional
            // z-scoring needed here, just the same clamp/map used everywhere else so
            // it's on the identical [0,1] scale as TrendRating before blending.
            double zRosterUnit = RatingScaling.ToUnitScale(
                (double)(record.ZRoster.Value * zRosterScalingConstant), mean: 0.0, stdDev: 0.25);

            // Simple average — ZRoster nudges the historical anchor rather than
            // overriding it. Weighting here is itself a candidate for future tuning.
            return (trendUnit + zRosterUnit) / 2.0;
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
