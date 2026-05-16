using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
{
    public class LinesRepository : ILinesRepository
    {
        private readonly NCAAContext _context;
        public LinesRepository(NCAAContext context) => _context = context;

        public Task<List<Lines>> GetByGameIdAsync(int gameId, CancellationToken token = default)
            => _context.Lines.Where(l => l.GameId == gameId).ToListAsync(token);

        public async Task<List<Lines>> GetByYearAndWeekAsync(int year, int week, CancellationToken token = default)
        {
            var gameIds = await _context.Games
                .Where(g => g.Year == year && g.Week == week)
                .Select(g => g.GameId)
                .ToListAsync(token);

            return await _context.Lines
                .Where(l => gameIds.Contains(l.GameId))
                .ToListAsync(token);
        }

        public async Task DeleteByGameIdAsync(int gameId, CancellationToken token = default)
        {
            var lines = await _context.Lines
                .Where(l => l.GameId == gameId)
                .ToListAsync(token);
            _context.Lines.RemoveRange(lines);
        }

        public Task AddRangeAsync(IEnumerable<Lines> lines, CancellationToken token = default)
        {
            _context.Lines.AddRange(lines);
            return Task.CompletedTask;
        }
    }
}
