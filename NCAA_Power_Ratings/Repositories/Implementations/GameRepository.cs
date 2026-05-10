using Microsoft.EntityFrameworkCore;
using NCAA_Power_Ratings.Data;
using NCAA_Power_Ratings.Models;
using NCAA_Power_Ratings.Repositories.Interfaces;

namespace NCAA_Power_Ratings.Repositories.Implementations
{
    public class GameRepository : IGameRepository
    {
        private readonly IDbContextFactory<NCAAContext> _contextFactory;

        public GameRepository(IDbContextFactory<NCAAContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<List<Game>> GetByYearAsync(
            int year,
            CancellationToken token = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(token);

            return await context.Game
                .Where(g => g.Year == year)
                .ToListAsync(token);
        }

        public async Task<List<Game>> GetByYearAndWeekAsync(
            int year,
            int week,
            CancellationToken token = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(token);

            return await context.Game
                .Where(g =>
                    g.Year == year &&
                    g.Week == week)
                .ToListAsync(token);
        }

        public async Task<List<Game>> GetPlayedGamesByYearAsync(
            int year,
            CancellationToken token = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(token);

            return await context.Game
                .Where(g =>
                    g.Year == year &&
                    (g.WPoints > 0 || g.LPoints > 0))
                .ToListAsync(token);
        }

        public async Task<List<Game>> GetPlayedGamesByYearAndWeekAsync(
            int year,
            int week,
            CancellationToken token = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(token);

            return await context.Game
                .Where(g =>
                    g.Year == year &&
                    g.Week <= week &&
                    (g.WPoints > 0 || g.LPoints > 0))
                .ToListAsync(token);
        }

        public async Task<Game?> GetByIdAsync(
            int gameId,
            CancellationToken token = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(token);

            return await context.Game
                .FirstOrDefaultAsync(g => g.Id == gameId, token);
        }

        public async Task AddRangeAsync(
            IEnumerable<Game> games,
            CancellationToken token = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(token);

            await context.Game.AddRangeAsync(games, token);
        }

        public async Task SaveChangesAsync(
            CancellationToken token = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(token);

            await context.SaveChangesAsync(token);
        }
    }
}