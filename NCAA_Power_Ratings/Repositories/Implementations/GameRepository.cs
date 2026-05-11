using Microsoft.EntityFrameworkCore;
using NCAA_Power_Ratings.Data;
using NCAA_Power_Ratings.Models;
using NCAA_Power_Ratings.Repositories.Interfaces;

namespace NCAA_Power_Ratings.Repositories.Implementations
{
    public class GameRepository : IGameRepository
    {
        private readonly NCAAContext _context;

        public GameRepository(NCAAContext context) => _context = context;

        public Task<List<Game>> GetByYearAsync(int year, CancellationToken token = default)
            => _context.Game
                .Where(g => g.Year == year)
                .ToListAsync(token);

        public Task<List<Game>> GetByYearAndWeekAsync(int year, int week, CancellationToken token = default)
            => _context.Game
                .Where(g => g.Year == year && g.Week == week)
                .ToListAsync(token);

        public Task<List<Game>> GetPlayedGamesByYearAsync(int year, CancellationToken token = default)
            => _context.Game
                .Where(g => g.Year == year && (g.WPoints > 0 || g.LPoints > 0))
                .ToListAsync(token);

        public Task<List<Game>> GetPlayedGamesByYearAndWeekAsync(int year, int week, CancellationToken token = default)
            => _context.Game
                .Where(g => g.Year == year &&
                            g.Week <= week &&
                            (g.WPoints > 0 || g.LPoints > 0))
                .ToListAsync(token);

        public Task<List<Game>> GetPlayedGamesSinceYearAsync(int fromYear, CancellationToken token = default)
            => _context.Game
                .Where(g => g.Year >= fromYear && (g.WPoints > 0 || g.LPoints > 0))
                .ToListAsync(token);

        public Task<Game?> GetByIdAsync(int gameId, CancellationToken token = default)
            => _context.Game
                .FirstOrDefaultAsync(g => g.Id == gameId, token);

        public async Task AddRangeAsync(IEnumerable<Game> games, CancellationToken token = default)
            => await _context.Game.AddRangeAsync(games, token);
        // SaveChanges is called through IUnitOfWork.SaveChangesAsync — not here.

        public Task<List<int>> GetPlayedWeeksByYearAsync(int year, CancellationToken token = default)
            => _context.Game
                .Where(g => g.Year == year && (g.WPoints > 0 || g.LPoints > 0))
                .Select(g => g.Week)
                .Distinct()
                .OrderBy(w => w)
                .ToListAsync(token);
    }
}
