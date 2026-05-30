using SaturdayPulse.Interfaces;
using SaturdayPulse.Contracts;
using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.Models;
using static SaturdayPulse.Interfaces.IAvgScoreDifferentialService;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Computes expected game distributions using the AvgScoreDifferential table.
    ///
    /// Fully migrated from legacy AvgScoreDelta bucket lookups:
    ///   - Expected margin from AvgScoreDifferential.AverageMargin (differential-based)
    ///   - StdDev from AvgScoreDifferential.StdDevMargin (differential-based)
    ///   - Direction encoded in the differential itself — no win-percentage flip needed
    ///
    /// Differential = ExpandStrength(teamStrength) - ExpandStrength(opponentStrength)
    ///   Positive → team is stronger → positive expected margin (team favored)
    ///   Negative → opponent is stronger → negative expected margin (opponent favored)
    ///
    /// Range: ±3.0 in 0.05 increments. Tail collapse at ±2.5/±2.75.
    /// Nearest bucket used when exact match not found.
    /// </summary>
    public class AvgScoreDifferentialService : IAvgScoreDifferentialService
    {
        private readonly IUnitOfWork _uow;

        public AvgScoreDifferentialService(IUnitOfWork uow) => _uow = uow;

        public ExpectedGameDistribution GetExpectedDistribution(
            double teamStrength, double opponentStrength)
            => GetExpectedDistribution((decimal)teamStrength, (decimal)opponentStrength);

        public ExpectedGameDistribution GetExpectedDistribution(
            decimal teamStrength, decimal opponentStrength)
        {
            var avgScoreDifferentials = _uow.Lookups
                .GetAvgScoreDifferentialsAsync()
                .GetAwaiter()
                .GetResult();

            // Differential encodes direction:
            // positive = team favored, negative = opponent favored.
            var differential = (decimal)GetStrengthDifferential(
                (double)teamStrength, (double)opponentStrength);

            // Find nearest bucket.
            var bucketRow = avgScoreDifferentials
                .OrderBy(b => Math.Abs(b.StrengthDifferential - differential))
                .FirstOrDefault();

            // Expected margin is already from team's perspective via differential sign.
            var expectedMargin = bucketRow != null
                ? (double)bucketRow.AverageMargin
                : AvgScoreDelta.DefaultAverageScoreDelta * Math.Sign((double)differential);

            var stdDev = bucketRow != null
                ? (double)bucketRow.StdDevMargin
                : AvgScoreDelta.DefaultStdDev;

            var sampleSize = bucketRow?.SampleSize ?? 0;

            return new ExpectedGameDistribution(
                ExpectedMargin: expectedMargin,
                StdDev:         stdDev,
                Reliability:    sampleSize > 100 ? 1.0 : sampleSize / 100.0,
                SampleSize:     (int)sampleSize);
        }

        public double GetStrengthDifferential(double teamStrength, double opponentStrength)
        {
            var expanded = RatingCalculator.ExpandStrength((decimal)teamStrength) -
                           RatingCalculator.ExpandStrength((decimal)opponentStrength);
            return NormalizeStrength((double)expanded);
        }

        public double NormalizeStrength(double strength)
            => Math.Max(-3.0, Math.Min(3.0, Math.Round(strength, 3)));
    }
}
