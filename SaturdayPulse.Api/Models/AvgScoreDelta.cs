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
        
        [NotMapped]
        public double ReliabilityWeight 
            => Math.Min(1.0, (double)SampleSize / ReliabilityThreshold);

        [NotMapped]
        public double WeightedAverageScoreDelta 
            => (ReliabilityWeight * (double)AverageScoreDelta) + ((1.0 - ReliabilityWeight) * DefaultAverageScoreDelta);

        [NotMapped]
        public double WeightedStdDev 
            => (ReliabilityWeight * (double)StDevP) + ((1.0 - ReliabilityWeight) * DefaultStdDev);

        //---Constants--//
        public const double DefaultAverageScoreDelta = 7.0;
        public const double DefaultStdDev = 14.0;
        public const int ReliabilityThreshold = 50;
    }
}