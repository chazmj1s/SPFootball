using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaturdayPulse.Models
{
    [Table("AvgScoreDeltas")]
    public class AvgScoreDelta
    {
        [Key]
        [Column("Id")]
        public int Id { get; set; }

        [Column("Team1WinPct", TypeName = "decimal(3,2)")]
        public decimal Team1WinPct { get; set; }

        [Column("Team2WinPct", TypeName = "decimal(3,2)")]
        public decimal Team2WinPct { get; set; }

        [Column("AverageScoreDelta", TypeName = "decimal(6,2)")]
        public decimal AverageScoreDelta { get; set; }

        [Column("StDevP", TypeName = "decimal(10,8)")]
        public decimal StDevP { get; set; }

        [Column("SampleSize")]
        public int? SampleSize { get; set; }
    }
}