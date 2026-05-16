using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
{
    public class GamesRepository : IGamesRepository
    {
        private readonly NCAAContext _context;
        public GamesRepository(NCAAContext context) => _context = context;

        public Task<List<Games>> GetByYearAsync(int year, CancellationToken token = default)
            => _context.Games.Where(g => g.Year == year).OrderBy(g => g.Week).ToListAsync(token);

        public Task<List<Games>> GetByYearAndWeekAsync(int year, int week, CancellationToken token = default)
            => _context.Games.Where(g => g.Year == year && g.Week == week).ToListAsync(token);

        public Task<Games?> GetByGameIdAsync(int gameId, CancellationToken token = default)
            => _context.Games.FirstOrDefaultAsync(g => g.GameId == gameId, token);

        public async Task UpsertAsync(Games game, CancellationToken token = default)
        {
            var existing = await _context.Games
                .FirstOrDefaultAsync(g => g.GameId == game.GameId, token);

            if (existing == null)
                _context.Games.Add(game);
            else
            {
                existing.HomePoints     = game.HomePoints;
                existing.AwayPoints     = game.AwayPoints;
                existing.Attendance     = game.Attendance;
                existing.NeutralSite    = game.NeutralSite;
                existing.ConferenceGame = game.ConferenceGame;
                existing.Venue          = game.Venue;
            }
        }

        public async Task UpsertRangeAsync(IEnumerable<Games> games, CancellationToken token = default)
        {
            var incoming    = games.ToList();
            var incomingIds = incoming.Select(g => g.GameId).ToHashSet();

            var existing = await _context.Games
                .Where(g => incomingIds.Contains(g.GameId))
                .ToDictionaryAsync(g => g.GameId, token);

            foreach (var game in incoming)
            {
                if (existing.TryGetValue(game.GameId, out var dbGame))
                {
                    dbGame.HomePoints     = game.HomePoints;
                    dbGame.AwayPoints     = game.AwayPoints;
                    dbGame.Attendance     = game.Attendance;
                    dbGame.NeutralSite    = game.NeutralSite;
                    dbGame.ConferenceGame = game.ConferenceGame;
                    dbGame.Venue          = game.Venue;
                }
                else
                    _context.Games.Add(game);
            }
        }
    }
}
