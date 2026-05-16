using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
{
    public interface ILinesRepository
    {
        Task<List<Lines>> GetByGameIdAsync(int gameId, CancellationToken token = default);
        Task<List<Lines>> GetByYearAndWeekAsync(int year, int week, CancellationToken token = default);
        Task DeleteByGameIdAsync(int gameId, CancellationToken token = default);
        Task AddRangeAsync(IEnumerable<Lines> lines, CancellationToken token = default);
    }
}
