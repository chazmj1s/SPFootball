using System.ComponentModel.DataAnnotations.Schema;

namespace SaturdayPulse.Models
{
    public class AvgScoreDifferential
    {
        public int Id { get; set; }

        // Rounded differential bucket
        public decimal StrengthDifferential { get; set; }

        // Historical observed values
        public decimal AverageMargin { get; set; }
        public decimal StdDevMargin { get; set; }

        // Optional but useful
        public decimal AverageTotalPoints { get; set; }

        // Reliability
        public int SampleSize { get; set; }

        // Metadata
        public DateTime LastUpdatedUtc { get; set; }

        // ---- Computed ----

        [NotMapped]
        public double ReliabilityWeight =>
            Math.Min(1.0,
                SampleSize / (double)ReliabilityThreshold);

        [NotMapped]
        public double WeightedAverageMargin =>
            (ReliabilityWeight * (double)AverageMargin)
            + ((1.0 - ReliabilityWeight) * DefaultAverageMargin);

        [NotMapped]
        public double WeightedStdDev =>
            (ReliabilityWeight * (double)StdDevMargin)
            + ((1.0 - ReliabilityWeight) * DefaultStdDev);

        // ---- Defaults ----

        public const double DefaultAverageMargin = 7.0;
        public const double DefaultStdDev = 14.0;
        public const int ReliabilityThreshold = 50;
    }
}
