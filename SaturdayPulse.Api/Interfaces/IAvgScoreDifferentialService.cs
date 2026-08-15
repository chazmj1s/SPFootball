using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.Models;

namespace SaturdayPulse.Interfaces
{
    public interface IAvgScoreDifferentialService
    {
        ExpectedGameDistribution GetExpectedDistribution(double teamStrength, double opponentStrength);
        ExpectedGameDistribution GetExpectedDistribution(decimal teamStrength, decimal opponentStrength);

        /// <summary>
        /// Same calculation as GetExpectedDistribution(double, double), but skips the
        /// internal per-call DB fetch of the AvgScoreDifferential table and reuses a
        /// pre-loaded set of buckets instead. For callers that need to invoke this many
        /// times in a tight loop (e.g. a calibration/backtest routine) — the other
        /// overloads' per-call fetch is fine for a single live prediction but not for
        /// thousands of calls in a grid search. Delegates to the exact same
        /// interpolation logic as the other overloads; no behavior difference, only
        /// where the buckets come from.
        /// </summary>
        ExpectedGameDistribution GetExpectedDistribution(double teamStrength, double opponentStrength, List<AvgScoreDifferential> buckets);

        double GetStrengthDifferential(double teamStrength,double opponentStrength);
        double NormalizeStrength(double strength);

    }
}
