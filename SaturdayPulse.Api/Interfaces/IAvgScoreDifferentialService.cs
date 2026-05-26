using SaturdayPulse.Contracts.Responses;

namespace SaturdayPulse.Interfaces
{
    public interface IAvgScoreDifferentialService
    {
        ExpectedGameDistribution GetExpectedDistribution(double teamStrength, double opponentStrength);
        ExpectedGameDistribution GetExpectedDistribution(decimal teamStrength, decimal opponentStrength);
        double GetStrengthDifferential(double teamStrength,double opponentStrength);
        double NormalizeStrength(double strength);

    }
}
