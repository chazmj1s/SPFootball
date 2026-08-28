using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
{
    public interface IAnchorBlendCoefficientRepository
    {
        Task AddAsync(AnchorBlendCoefficient coefficient, CancellationToken token = default);

        Task<List<AnchorBlendCoefficient>> GetBySeasonAsync(int season, CancellationToken token = default);

        Task<AnchorBlendCoefficient?> GetLatestBySeasonAsync(int season, CancellationToken token = default);
    }
}
