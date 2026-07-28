using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaturdayPulse.Models
{
    /// <summary>
    /// Stores historical performance statistics for specific team-vs-team matchups.
    /// Used to detect high-variance rivalries by comparing actual matchup variance
    /// to expected variance from the live AvgScoreDifferential baseline.
    /// </summary>
    [Table("MatchupHistory")]
    public class MatchupHistory
    {
        /// <summary>
        /// First team in the matchup (lower team ID for consistency).
        /// </summary>
        [Key]
        [Column("Team1Id", Order = 0)]
        public int Team1Id { get; set; }

        /// <summary>
        /// Second team in the matchup (higher team ID for consistency).
        /// </summary>
        [Key]
        [Column("Team2Id", Order = 1)]
        public int Team2Id { get; set; }

        /// <summary>
        /// Total number of games played between these teams in the database.
        /// Minimum threshold (e.g., 10) required for statistical significance.
        /// </summary>
        [Column("GamesPlayed")]
        public int GamesPlayed { get; set; }

        /// <summary>
        /// Average margin of victory (absolute value) across all games.
        /// </summary>
        [Column("AvgMargin", TypeName = "decimal(5,2)")]
        public decimal AvgMargin { get; set; }

        /// <summary>
        /// Standard deviation of victory margins.
        /// High values indicate unpredictable/chaotic matchups (likely rivalries).
        /// </summary>
        [Column("StDevMargin", TypeName = "decimal(5,2)")]
        public decimal StDevMargin { get; set; }

        /// <summary>
        /// Average combined total points (both teams) across all games in this matchup.
        /// Added to support a real, data-driven RivalryScoringAdjustment — replaces a
        /// prior hand-picked EPIC/NATIONAL/STATE scoring-reduction guess with this
        /// pair's own historical scoring level, compared against the live
        /// AvgScoreDifferential.AverageTotalPoints for their current strength
        /// differential at prediction time.
        /// </summary>
        [Column("AvgTotalPoints", TypeName = "decimal(6,2)")]
        public decimal AvgTotalPoints { get; set; }

        /// <summary>
        /// Percentage of games won by the team with fewer season wins (upset rate).
        /// Range: 0.00 to 1.00 (e.g., 0.35 = 35% upset rate).
        /// </summary>
        [Column("UpsetRate", TypeName = "decimal(4,3)")]
        public decimal UpsetRate { get; set; }

        /// <summary>
        /// Most recent year these teams played each other.
        /// </summary>
        [Column("LastPlayed")]
        public int LastPlayed { get; set; }

        /// <summary>
        /// First year these teams played each other (for longevity tracking).
        /// </summary>
        [Column("FirstPlayed")]
        public int FirstPlayed { get; set; }

        /// <summary>
        /// Optional rivalry name if this is a recognized rivalry.
        /// Example: "The Game", "Iron Bowl", "Red River Rivalry"
        /// </summary>
        [Column("RivalryName", TypeName = "varchar(100)")]
        public string? RivalryName { get; set; }

        /// <summary>
        /// Optional rivalry tier indicating intensity level.
        /// EPIC = highest variance, NATIONAL = high variance, STATE = moderate variance.
        /// No longer drives RivalryVarianceMultiplierForDisplay/RivalryScoringAdjustment
        /// directly (those now compute a live ratio against this row's own
        /// StDevMargin/AvgTotalPoints) — retained for display/labeling purposes only.
        /// </summary>
        [Column("RivalryTier", TypeName = "varchar(20)")]
        public string? RivalryTier { get; set; }

        /// <summary>
        /// RETIRED — never populated by MatchupHistoryCalculator (confirmed by reading
        /// the calculator directly: it sets every other field on this entity except
        /// this one). Left in place as a NotMapped property rather than removed,
        /// per the project's convention of leaving retired fields intact. The live
        /// replacement is computed on demand in RatingCalculator using this row's
        /// StDevMargin against AvgScoreDifferential's interpolated StdDev for the
        /// pair's current differential — not stored here.
        /// </summary>
        [NotMapped]
        public double VarianceRatio { get; set; }
    }
}
