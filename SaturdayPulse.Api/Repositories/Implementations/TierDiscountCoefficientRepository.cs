using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
{
    public class TierDiscountCoefficientRepository : ITierDiscountCoefficientRepository
    {
        private readonly NCAAContext _context;

        public TierDiscountCoefficientRepository(NCAAContext context) => _context = context;

        public async Task AddAsync(TierDiscountCoefficient coefficient, CancellationToken token = default)
            => await _context.TierDiscountCoefficients.AddAsync(coefficient, token);

        public Task<List<TierDiscountCoefficient>> GetBySeasonAsync(int season, CancellationToken token = default)
            => _context.TierDiscountCoefficients
                .Where(c => c.Season == season)
                .OrderBy(c => c.ComputedAt)
                .ToListAsync(token);

        public Task<TierDiscountCoefficient?> GetLatestBySeasonAsync(int season, CancellationToken token = default)
            => _context.TierDiscountCoefficients
                .Where(c => c.Season == season)
                .OrderByDescending(c => c.ComputedAt)
                .FirstOrDefaultAsync(token);
    }
}
