using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
{
    public class PlayerStatRepository : IPlayerStatRepository
    {
        private readonly NCAAContext _context;

        public PlayerStatRepository(NCAAContext context) => _context = context;

        public Task<List<PlayerStat>> GetBySeasonAsync(int season, CancellationToken token = default)
            => _context.PlayerStats
                .Where(s => s.Season == season)
                .ToListAsync(token);

        public Task<List<PlayerStat>> GetByTeamAndSeasonAsync(string team, int season, CancellationToken token = default)
            => _context.PlayerStats
                .Where(s => s.Team == team && s.Season == season)
                .ToListAsync(token);

        public Task<List<PlayerStat>> GetByPlayerAndSeasonAsync(string playerId, int season, CancellationToken token = default)
            => _context.PlayerStats
                .Where(s => s.PlayerId == playerId && s.Season == season)
                .ToListAsync(token);

        public Task<List<int>> GetDistinctSeasonsAsync(CancellationToken token = default)
            => _context.PlayerStats
                .Select(s => s.Season)
                .Distinct()
                .OrderBy(s => s)
                .ToListAsync(token);

        public async Task UpsertSeasonAsync(int season, List<PlayerStat> entries, CancellationToken token = default)
        {
            var existing = await _context.PlayerStats
                .Where(s => s.Season == season)
                .ToListAsync(token);

            if (existing.Any())
                _context.PlayerStats.RemoveRange(existing);

            // No SaveChangesAsync here — see RosterPlayerRepository for why.
            await _context.PlayerStats.AddRangeAsync(entries, token);
        }
    }
}
