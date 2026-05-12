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

        [Column("Week", TypeName = "tinyint")]
        public byte Week { get; set; }

        [Column("Wins", TypeName = "tinyint")]
        public byte Wins { get; set; }

        [Column("Losses", TypeName = "tinyint")]
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
        public Team? Team { get; set; }

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
    }
}
