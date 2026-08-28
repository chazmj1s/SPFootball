using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaturdayPulse.Models
{
    /// <summary>
    /// Persisted ZRoster/SeedRating blend weights for RatingBlendingService.
    /// ComputeSeededAnchorUnit, one row per computation. Computed by
    /// AnchorBlendCalculator using a TRAILING window (not full history — see
    /// remarks below) of real games, and intended to replace the prior
    /// hardcoded 50/50 split + arbitrary zRosterScalingConstant/stdDev=0.25
    /// compression that were both dev placeholders, never validated (see
    /// RatingBlendingService class remarks, 2026-08-20).
    ///
    /// ROLLING, not append-and-grow like TierDiscountCoefficient — deliberately
    /// windowed to the trailing 3 seasons (ComputedThroughYear - 2 through
    /// ComputedThroughYear) rather than accumulating since 1965. Confirmed
    /// with Charlie: the transfer portal era means a team's roster can change
    /// dramatically in a single offseason in a way it couldn't pre-portal, so
    /// ZRoster's relative importance is expected to keep drifting — a rolling
    /// window tracks that drift; a fixed constant or growing-window average
    /// would smooth it away. Empirically confirmed in the calibration data
    /// itself: the ZRoster/SeedRating standardized-weight ratio was ~3.98x
    /// over the full 2021-2025 history but ~2.98x over just the trailing
    /// 2023-2025 window — a real, non-trivial shift, not noise.
    ///
    /// SeedRating, not TrendRating — chosen specifically to window-match the
    /// trailing regression: TrendRating is itself a 5-year window, which
    /// would outlive a 3-year rolling recalibration and quietly pull in data
    /// from outside the period being fit. SeedRating (Trend + Pedigree blend)
    /// also fit marginally but consistently better in backtesting (R^2 ~0.184
    /// vs ~0.178 for TrendRating over the same trailing window). This means
    /// ComputeSeededAnchorUnit's non-roster term changes from TrendRating to
    /// SeedRating too — see RatingBlendingService remarks; using TrendRating
    /// in production while calibrating against SeedRating would be internally
    /// inconsistent.
    ///
    /// UPSERT by Season — deliberately NOT append-only like TierDiscountCoefficient.
    /// Rerunning ComputeAndPersistCoefficientsAsync for a season that already has a
    /// row updates that row in place (same Id, refreshed ComputedAt) rather than
    /// inserting a duplicate. This is a genuine departure from TierDiscountCoefficient's
    /// convention, not an oversight — TierDiscountCoefficient's history-preservation
    /// argument (drift across eras staying visible) doesn't carry the same value here:
    /// a rolling 3-year window recomputed for the same season should always converge
    /// on the same answer from the same underlying games, so stale duplicate rows from
    /// a rerun have no diagnostic value, just clutter. Confirmed with Charlie
    /// 2026-08-21 after a real backfill run produced duplicate rows per season.
    ///
    /// Consumers can safely assume at most one row per Season.
    /// </summary>
    [Table("AnchorBlendCoefficients")]
    public class AnchorBlendCoefficient
    {
        [Key]
        [Column("Id")]
        public int Id { get; set; }

        /// <summary>The season these weights should be APPLIED to — not
        /// necessarily the year this row was computed in.</summary>
        [Column("Season")]
        public int Season { get; set; }

        /// <summary>Trailing window start — always ComputedThroughYear - (WindowYears - 1).</summary>
        [Column("ComputedFromStartYear")]
        public int ComputedFromStartYear { get; set; }

        [Column("ComputedThroughYear")]
        public int ComputedThroughYear { get; set; }

        /// <summary>Window length in years actually used (3, per the confirmed
        /// rolling-window decision) — persisted rather than assumed, so a
        /// future change to the window length is visible in history rather
        /// than silently reinterpreting old rows.</summary>
        [Column("WindowYears")]
        public int WindowYears { get; set; }

        /// <summary>
        /// Normalized weight for the ZRoster term in ComputeSeededAnchorUnit's
        /// blend — derived from the regression's standardized coefficients,
        /// NOT the raw regression coefficient (raw coefficients aren't
        /// comparable across ZRoster and SeedRating since they sit on very
        /// different scales — see AnchorBlendCalculator remarks).
        /// ZRosterWeight + RatingWeight always sums to 1.0.
        /// </summary>
        [Column("ZRosterWeight", TypeName = "decimal(10,4)")]
        public decimal ZRosterWeight { get; set; }

        [Column("RatingWeight", TypeName = "decimal(10,4)")]
        public decimal RatingWeight { get; set; }

        /// <summary>
        /// This window's real ZRoster mean/stddev — replaces the old
        /// hardcoded mean=0/stdDev=0.25 compression in ComputeSeededAnchorUnit
        /// with the actual distribution ZRoster had over the games this row
        /// was calibrated from, so the [0,1] unit-scale mapping is honest
        /// rather than an arbitrary guess.
        /// </summary>
        [Column("ZRosterMean", TypeName = "decimal(10,4)")]
        public decimal ZRosterMean { get; set; }

        [Column("ZRosterStdDev", TypeName = "decimal(10,4)")]
        public decimal ZRosterStdDev { get; set; }

        /// <summary>Root-mean-squared per-game margin error at the fitted
        /// (ZRosterWeight, RatingWeight) — same diagnostic role as
        /// TierDiscountCoefficient.TypicalPredictionErrorPoints.</summary>
        [Column("TypicalPredictionErrorPoints", TypeName = "decimal(10,4)")]
        public decimal TypicalPredictionErrorPoints { get; set; }

        [Column("GamesUsed")]
        public int GamesUsed { get; set; }

        [Column("ComputedAt")]
        public DateTime ComputedAt { get; set; }
    }
}
