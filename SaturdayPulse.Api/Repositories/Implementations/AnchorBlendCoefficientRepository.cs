using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Api.Repositories.Implementations
{
    public class AnchorBlendCoefficientRepository : IAnchorBlendCoefficientRepository
    {
        private readonly NCAAContext _context;
        public AnchorBlendCoefficientRepository(NCAAContext context) => _context = context;

        public async Task AddAsync(AnchorBlendCoefficient coefficient, CancellationToken token)
            => await _context.AnchorBlendCoefficients.AddAsync(coefficient, token);

        public Task<List<AnchorBlendCoefficient>> GetBySeasonAsync(int season, CancellationToken token)
            => _context.AnchorBlendCoefficients
                .Where(c => c.Season == season)
                .OrderBy(c => c.ComputedAt)
                .ToListAsync(token);

        public Task<AnchorBlendCoefficient?> GetLatestBySeasonAsync(int season, CancellationToken token)
            => _context.AnchorBlendCoefficients
                .Where(c => c.Season == season)
                .OrderByDescending(c => c.ComputedAt)
                .FirstOrDefaultAsync(token);
    }
}
