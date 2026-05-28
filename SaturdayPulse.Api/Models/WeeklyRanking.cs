using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaturdayPulse.Models
{
    /// <summary>
    /// Pre-computed per-week power rankings snapshot.
    /// Populated by WeeklyRankingsService after each week's games are finalized.
    /// </summary>
    [Table("WeeklyRankings")]
    public class WeeklyRanking
    {
        [Key]
        [Column("Id")]
        public int Id { get; set; }

        [Column("TeamID")]
        public int TeamID { get; set; }

        [Column("Year", TypeName = "smallint")]
        public short Year { get; set; }

        [Column("Week", TypeName = "smallint")]
        public byte Week { get; set; }

        [Column("Wins", TypeName = "smallint")]
        public byte Wins { get; set; }

        [Column("Losses", TypeName = "smallint")]
        public byte Losses { get; set; }

        [Column("PointsFor")]
        public int PointsFor { get; set; }

        [Column("PointsAgainst")]
        public int PointsAgainst { get; set; }

        [Column("BaseSOS", TypeName = "decimal(10,3)")]
        public decimal? BaseSOS { get; set; }

        [Column("SubSOS", TypeName = "decimal(10,3)")]
        public decimal? SubSOS { get; set; }

        [Column("CombinedSOS", TypeName = "decimal(10,4)")]
        public decimal? CombinedSOS { get; set; }

        [Column("PowerRating", TypeName = "decimal(10,4)")]
        public decimal? PowerRating { get; set; }

        [Column("Ranking", TypeName = "decimal(10,4)")]
        public decimal? Ranking { get; set; }

        [Column("OverallRank")]
        public int OverallRank { get; set; }

        [Column("TierRank")]
        public int TierRank { get; set; }

        [ForeignKey("TeamID")]
        public Teams? Team { get; set; }

        [Column("AvgPointsScored", TypeName = "decimal(5,2)")]
        public decimal AvgPointsScored { get; set; }

        [Column("AvgPointsAllowed", TypeName = "decimal(5,2)")]
        public decimal AvgPointsAllowed { get; set; }

        [Column("OffensiveZScore", TypeName = "decimal(7,4)")]
        public decimal OffensiveZScore { get; set; }

        [Column("DefensiveZScore", TypeName = "decimal(7,4)")]
        public decimal DefensiveZScore { get; set; }

        [Column("OffensiveRank")]
        public int OffensiveRank { get; set; }

        [Column("DefensiveRank")]
        public int DefensiveRank { get; set; }

        /// <summary>
        /// Absolute quality of incoming portal transfers for this season,
        /// weighted by position tier and normalized against league mean.
        /// Copied from TeamRecord.RosterStrength into the week 0 snapshot by
        /// InitializeSeasonAsync. Used by GamePredictionService via WeeklyRankings
        /// to adjust week 0 PowerRating before any games are played.
        /// Null for years before portal data exists (pre-2021) and for weeks > 0.
        /// </summary>
        [Column("RosterStrength", TypeName = "decimal(10,4)")]
        public decimal? RosterStrength { get; set; }
    }
}
