using SaturdayPulse.Interfaces;
using SaturdayPulse.Contracts;
using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.Models;
using static SaturdayPulse.Interfaces.IAvgScoreDifferentialService;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Computes expected game distributions using the AvgScoreDifferential table —
    /// 60+ years of real historical outcomes binned by strength differential. This is
    /// the core historical baseline for the prediction system and is not second-guessed
    /// or smoothed here; only HOW a value is read out of the table has changed.
    ///
    /// REBUILT: nearest-bucket lookup (OrderBy(Math.Abs(diff)).FirstOrDefault()) has
    /// been replaced with true linear interpolation between the two buckets bracketing
    /// the computed differential on the 0.05 grid. Nearest-neighbor was silently
    /// snapping to whichever bucket happened to be closest and discarding real
    /// precision the table already provides between grid points.
    ///
    /// AverageTotalPoints is now surfaced on ExpectedGameDistribution — it existed on
    /// the AvgScoreDifferential entity but was previously read from the DB row and
    /// discarded before reaching any caller.
    ///
    /// Reliability is now the AvgScoreDifferential entity's own ReliabilityWeight
    /// (SampleSize / ReliabilityThreshold, already defined on the entity), taken from
    /// the WEAKER of the two bracketing buckets so a well-sampled neighbor never masks
    /// a thin one. This replaces the previous ad hoc `sampleSize > 100 ? 1.0 :
    /// sampleSize / 100.0` formula, which used a different, disconnected threshold (100)
    /// than the one already living on the entity (50) — and which was never actually
    /// consumed by GamePredictionService anyway. It's consumed now.
    ///
    /// Differential = ExpandStrength(teamStrength) - ExpandStrength(opponentStrength)
    ///   Positive → team is stronger → positive expected margin (team favored)
    ///   Negative → opponent is stronger → negative expected margin (opponent favored)
    ///
    /// Range: ±3.0 in 0.05 increments.
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
            var buckets = _uow.Lookups
                .GetAvgScoreDifferentialsAsync()
                .GetAwaiter()
                .GetResult();

            // Differential encodes direction:
            // positive = team favored, negative = opponent favored.
            var differential = (decimal)GetStrengthDifferential(
                (double)teamStrength, (double)opponentStrength);

            return InterpolateDistribution(buckets, differential);
        }

        /// <summary>
        /// Linearly interpolates AverageMargin, StdDevMargin, and AverageTotalPoints
        /// between the two buckets bracketing the given differential. Falls back to a
        /// single bucket when the differential lands exactly on a grid point, or when
        /// it's beyond the table's range on one side (no extrapolation past real data —
        /// the edge bucket is used as-is). SampleSize is the min of the two brackets
        /// (a fractional sample count isn't meaningful); Reliability is the weaker
        /// (lower ReliabilityWeight) of the two, for the same reason.
        /// </summary>
        private static ExpectedGameDistribution InterpolateDistribution(
            List<AvgScoreDifferential> buckets, decimal differential)
        {
            if (buckets == null || buckets.Count == 0)
                return new ExpectedGameDistribution(
                    ExpectedMargin:     AvgScoreDifferential.DefaultAverageMargin * Math.Sign((double)differential),
                    StdDev:             AvgScoreDifferential.DefaultStdDev,
                    Reliability:        0.0,
                    SampleSize:         0,
                    AverageTotalPoints: DefaultAverageTotalPoints);

            var ordered = buckets.OrderBy(b => b.StrengthDifferential).ToList();

            var lower = ordered.LastOrDefault(b => b.StrengthDifferential <= differential) ?? ordered.First();
            var upper = ordered.FirstOrDefault(b => b.StrengthDifferential >= differential) ?? ordered.Last();

            double margin, stdDev, totalPoints;
            int sampleSize;
            double reliability;

            if (upper.StrengthDifferential == lower.StrengthDifferential)
            {
                // Exact grid match (or the differential sits outside the table's range,
                // in which case lower/upper are the same edge bucket) — no interpolation.
                margin      = (double)lower.AverageMargin;
                stdDev      = (double)lower.StdDevMargin;
                totalPoints = (double)lower.AverageTotalPoints;
                sampleSize  = lower.SampleSize;
                reliability = lower.ReliabilityWeight;
            }
            else
            {
                var t = (double)((differential - lower.StrengthDifferential) /
                                  (upper.StrengthDifferential - lower.StrengthDifferential));

                margin      = Lerp((double)lower.AverageMargin,      (double)upper.AverageMargin,      t);
                stdDev      = Lerp((double)lower.StdDevMargin,       (double)upper.StdDevMargin,       t);
                totalPoints = Lerp((double)lower.AverageTotalPoints, (double)upper.AverageTotalPoints,  t);
                sampleSize  = Math.Min(lower.SampleSize, upper.SampleSize);
                reliability = Math.Min(lower.ReliabilityWeight, upper.ReliabilityWeight);
            }

            return new ExpectedGameDistribution(
                ExpectedMargin:     Math.Round(margin, 2),
                StdDev:             Math.Round(stdDev, 2),
                Reliability:        reliability,
                SampleSize:         sampleSize,
                AverageTotalPoints: Math.Round(totalPoints, 2));
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        /// <summary>
        /// Only reachable if the AvgScoreDifferential table is completely empty (never
        /// seeded). Not a tuned prediction constant — a placeholder so the app doesn't
        /// throw before real data is loaded. Should never fire against production data.
        /// </summary>
        private const double DefaultAverageTotalPoints = 56.0;

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
