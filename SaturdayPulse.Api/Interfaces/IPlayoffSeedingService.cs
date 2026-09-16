using SaturdayPulse.Models;

namespace SaturdayPulse.Interfaces
{
    public interface IPlayoffSeedingService
    {
        Task<PlayoffFieldResult> GetProjectedFieldAsync(
            int year, int week, CancellationToken token = default);

        Task<PlayoffBracketResult> GetProjectedBracketAsync(
            int year, int week, CancellationToken token = default);
    }
}
