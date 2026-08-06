using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Contracts;
using SaturdayPulse.Data;
using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories
{
    /// <summary>
    /// EF Core repository for the read-only ResolvedGameResults view.
    /// See ResolvedGameResult remarks — never Add/Update/Remove here.
    /// </summary>
    public class ResolvedGameResultRepository : IResolvedGameResultRepository
    {
        private readonly NCAAContext _ctx;

        public ResolvedGameResultRepository(NCAAContext ctx) => _ctx = ctx;

        public Task<List<ResolvedGameResult>> GetByYearAsync(
            int year, CancellationToken token = default)
            => _ctx.ResolvedGameResults
                   .Where(r => r.Year == year)
                   .ToListAsync(token);

        public Task<List<ResolvedGameResult>> GetByYearThroughWeekAsync(
            int year, int week, CancellationToken token = default)
            => _ctx.ResolvedGameResults
                   .Where(r => r.Year == year && r.Week <= week)
                   .ToListAsync(token);
    }
}
