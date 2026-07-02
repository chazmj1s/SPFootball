using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
{
    public class RosterPlayerRepository : IRosterPlayerRepository
    {
        private readonly NCAAContext _context;

        public RosterPlayerRepository(NCAAContext context) => _context = context;

        public Task<List<RosterPlayer>> GetBySeasonAsync(int season, CancellationToken token = default)
            => _context.RosterPlayers
                .Where(r => r.Season == season)
                .ToListAsync(token);

        public Task<List<RosterPlayer>> GetByTeamAndSeasonAsync(string team, int season, CancellationToken token = default)
            => _context.RosterPlayers
                .Where(r => r.Team == team && r.Season == season)
                .ToListAsync(token);

        public Task<List<int>> GetDistinctSeasonsAsync(CancellationToken token = default)
            => _context.RosterPlayers
                .Select(r => r.Season)
                .Distinct()
                .OrderBy(s => s)
                .ToListAsync(token);

        public async Task UpsertSeasonAsync(int season, List<RosterPlayer> entries, CancellationToken token = default)
        {
            var existing = await _context.RosterPlayers
                .Where(r => r.Season == season)
                .ToListAsync(token);

            if (existing.Any())
                _context.RosterPlayers.RemoveRange(existing);

            await _context.RosterPlayers.AddRangeAsync(entries, token);
            await _context.SaveChangesAsync(token);
        }
    }
}
