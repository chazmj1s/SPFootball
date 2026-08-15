using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaturdayPulse.Models
{
    /// <summary>
    /// Persisted G6/P4 discount coefficients, one row per computation. Computed by
    /// TierDiscountCalculator (Method B — see its remarks for the full methodology)
    /// using only games played through the prior season, and intended to be applied at
    /// Projection build time for Tier1-vs-Tier2 matchups only.
    ///
    /// Append-only: a season is recomputed and a NEW row inserted each time
    /// RunSeasonSetupAsync runs, rather than overwriting the prior row. The underlying
    /// T1/T2 relationship is known to drift over time (see TierDiscountCalculator
    /// remarks on the retired Method C — its rolling-window output showed the ratio
    /// moving from ~0.95 to ~1.18 across eras) — keeping history here means that drift
    /// stays visible in the data rather than being silently overwritten every year.
    /// Consumers should read the most recent row for a given Season (highest
    /// ComputedAt), not assume there's exactly one.
    /// </summary>
    [Table("TierDiscountCoefficients")]
    public class TierDiscountCoefficient
    {
        [Key]
        [Column("Id")]
        public int Id { get; set; }

        /// <summary>The season these coefficients should be APPLIED to — not
        /// necessarily the year this row was computed in.</summary>
        [Column("Season")]
        public int Season { get; set; }

        [Column("ComputedFromStartYear")]
        public int ComputedFromStartYear { get; set; }

        [Column("ComputedThroughYear")]
        public int ComputedThroughYear { get; set; }

        /// <summary>
        /// Discount applied to a Tier 2 team's win differential before comparing it to
        /// a Tier 1 team's — e.g. 0.81 means a G6 team's record is treated as ~81% of
        /// face value when estimating a cross-tier margin from the record gap alone.
        /// </summary>
        [Column("WinDifferentialDiscount", TypeName = "decimal(10,4)")]
        public decimal WinDifferentialDiscount { get; set; }

        /// <summary>
        /// Flat point adjustment applied to every Tier1-vs-Tier2 prediction, on top of
        /// the record-based term — the portion of the real cross-tier margin that does
        /// NOT scale with either team's win-loss record at all.
        /// </summary>
        [Column("CaliberGapPoints", TypeName = "decimal(10,4)")]
        public decimal CaliberGapPoints { get; set; }

        /// <summary>
        /// Root-mean-squared per-game prediction error, in points, at
        /// (WinDifferentialDiscount, CaliberGapPoints) — how far off a typical single
        /// game's prediction still is even after fitting both parameters. NOT the same
        /// thing as the average error, which is ~0 by construction (see
        /// TierDiscountCalculator remarks).
        /// </summary>
        [Column("TypicalPredictionErrorPoints", TypeName = "decimal(10,4)")]
        public decimal TypicalPredictionErrorPoints { get; set; }

        [Column("GamesUsed")]
        public int GamesUsed { get; set; }

        [Column("ComputedAt")]
        public DateTime ComputedAt { get; set; }
    }
}
