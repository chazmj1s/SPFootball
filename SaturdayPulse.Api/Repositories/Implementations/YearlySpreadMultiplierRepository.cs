using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
{
    public class YearlySpreadMultiplierRepository : IYearlySpreadMultiplierRepository
    {

        private readonly NCAAContext _ctx;

        public YearlySpreadMultiplierRepository(NCAAContext ctx) => _ctx = ctx;

        public async Task<List<YearlySpreadMultiplier>> GetByYearAsync(int year, CancellationToken token = default)
        {
            return await _ctx.Set<YearlySpreadMultiplier>().Where(x => x.Targetyear == year).ToListAsync(token);
        }

        public async Task<List<YearlySpreadMultiplier>> GetAllAsync(CancellationToken token = default)
            => await _ctx.Set<YearlySpreadMultiplier>().ToListAsync(token);
    }
}
