using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaturdayPulse.Models
{
    [Table("TeamRecords")]
    public class TeamRecord
    {
        [Key]
        [Column("Id")]
        public int Id { get; set; }

        [Column("TeamID")]
        public int TeamID { get; set; }

        [Column("Year", TypeName = "smallint")]
        public short Year { get; set; }

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

        [ForeignKey("TeamID")]
        public Teams? Teams { get; set; }

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

        [Column("SeedRating", TypeName = "decimal(10,4)")]
        public decimal? SeedRating { get; set; }      // 3-year weighted (50/30/20)

        [Column("TrendRating", TypeName = "decimal(10,4)")]
        public decimal? TrendRating { get; set; }     // 5-year weighted (40/25/15/12/8)

        [Column("PedigreeRating", TypeName = "decimal(10,4)")]
        public decimal? PedigreeRating { get; set; }  // 10-year linear decay

        [NotMapped]
        public List<double>? TrendHistory { get; set; }

        [NotMapped]
        public List<double>? PedigreeHistory { get; set; }

        [NotMapped]
        public int RegularSeasonGames => Year switch
        {
            >= 2006 => 12,
            >= 1980 => 11,
            >= 1965 => 10,
            _ => 12
        };
    }
}
