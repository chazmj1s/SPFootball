using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
{
    public interface IYearlySpreadMultiplierRepository
    {
        Task<List<YearlySpreadMultiplier>> GetByYearAsync(
            int year, CancellationToken token = default);
        Task<List<YearlySpreadMultiplier>> GetAllAsync(
            CancellationToken token = default);
    }
}
