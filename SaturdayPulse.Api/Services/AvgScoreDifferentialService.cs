using SaturdayPulse.Interfaces;
using SaturdayPulse.Contracts;
using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.Models;
using static SaturdayPulse.Interfaces.IAvgScoreDifferentialService;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Transitional implementation:
    /// Uses legacy AvgScoreDelta bucket lookups internally
    /// while exposing the future differential-based API surface.
    ///
    /// Future versions will:
    ///   - replace dual-bucket lookups with differential buckets
    ///   - add interpolation/smoothing
    ///   - support latent strength ratings
    /// without requiring callers to change.
    /// </summary>
    public class AvgScoreDifferentialService : IAvgScoreDifferentialService
    {
        private readonly IUnitOfWork _uow;

    public AvgScoreDifferentialService(IUnitOfWork uow)
        {
            _uow = uow;
        }

        public ExpectedGameDistribution GetExpectedDistribution(double teamStrength, double opponentStrength)
        {
            return GetExpectedDistribution((decimal)teamStrength, (decimal)opponentStrength);
        }

        public ExpectedGameDistribution GetExpectedDistribution(decimal teamStrength, decimal opponentStrength)
        {
            // Normalize strengths
            teamStrength = (decimal)NormalizeStrength((double)teamStrength);
            opponentStrength = (decimal)NormalizeStrength((double)opponentStrength);

            // Legacy ASD lookup still uses higher/lower bucket ordering
            var maxStrength = Math.Max(teamStrength, opponentStrength);
            var minStrength = Math.Min(teamStrength, opponentStrength);

            var avgScoreDeltas = _uow.Lookups
                .GetAvgScoreDeltasAsync()
                .GetAwaiter()
                .GetResult();

            var asd = avgScoreDeltas.FirstOrDefault(a =>
                          a.Team1WinPct == maxStrength &&
                          a.Team2WinPct == minStrength)
                      ?? new AvgScoreDelta
                      {
                          Team1WinPct = maxStrength,
                          Team2WinPct = minStrength,
                          AverageScoreDelta = (decimal)AvgScoreDelta.DefaultAverageScoreDelta,
                          StDevP = (decimal)AvgScoreDelta.DefaultStdDev,
                          SampleSize = 0
                      };

            // Differential-based lookup
            var differential = GetStrengthDifferential((double)teamStrength, (double)opponentStrength);

            var avgScoreDifferentials = _uow.Lookups
                .GetAvgScoreDifferentialsAsync()
                .GetAwaiter()
                .GetResult();

            var smoothedExpectedMargin = RatingCalculator.GetSmoothedExpectedMargin(avgScoreDifferentials, (decimal)differential);

            // Preserve directional perspective
            var expectedMargin = teamStrength >= opponentStrength ? smoothedExpectedMargin : -smoothedExpectedMargin;

            return new ExpectedGameDistribution(
                ExpectedMargin: expectedMargin,
                StdDev: asd.WeightedStdDev,
                Reliability: asd.ReliabilityWeight,
                SampleSize: (int)asd.SampleSize);
        }

        public double GetStrengthDifferential(double teamStrength, double opponentStrength)
        {
            var expanded = RatingCalculator.ExpandStrength((decimal)teamStrength) -
                   RatingCalculator.ExpandStrength((decimal)opponentStrength);
            return (double)NormalizeStrength((double)expanded);
        }

        public double NormalizeStrength(double strength)
        {
            return Math.Max(-3.0, Math.Min(3.0, Math.Round(strength, 3)));
        }
    }
}
