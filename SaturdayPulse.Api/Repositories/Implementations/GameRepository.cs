using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
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

        public Task<List<Game>> GetByYearUpToWeekAsync(int year, int maxWeek, CancellationToken token = default)
            => _context.Game
                .Where(g => g.Year == year && g.Week <= maxWeek)
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

        public Task<List<int>> GetPlayedWeeksByYearAsync(int year, CancellationToken token = default)
            => _context.Game
                .Where(g => g.Year == year && (g.WPoints > 0 || g.LPoints > 0))
                .Select(g => g.Week)
                .Distinct()
                .OrderBy(w => w)
                .ToListAsync(token);

        public Task<List<Game>> GetRivalryHistoryAsync(
            int team1Id, int team2Id, int fromYear, CancellationToken token = default)
            => _context.Game
                .Where(g => g.Year >= fromYear &&
                            ((g.WinnerId == team1Id && g.LoserId == team2Id) ||
                             (g.WinnerId == team2Id && g.LoserId == team1Id)))
                .OrderBy(g => g.Year)
                .ThenBy(g => g.Week)
                .ToListAsync(token);

        public Task<Game?> GetByIdAsync(int gameId, CancellationToken token = default)
            => _context.Game
                .FirstOrDefaultAsync(g => g.Id == gameId, token);

        public async Task AddRangeAsync(IEnumerable<Game> games, CancellationToken token = default)
            => await _context.Game.AddRangeAsync(games, token);

        public async Task<List<GameParticipant>> GetGameParticipantsAsync(
            int year, CancellationToken token = default)
        {
            var fromWinner = from g in _context.Game
                where g.Year == year
                join t   in _context.Team on g.WinnerId equals t.TeamID
                join opp in _context.Team on g.LoserId  equals opp.TeamID
                select new GameParticipant
                {
                    TeamId           = g.WinnerId,
                    TeamDivision     = t.Division,
                    OpponentId       = g.LoserId,
                    OpponentDivision = opp.Division,
                    TeamPoints       = g.WPoints,
                    OpponentPoints   = g.LPoints,
                    Location         = g.Location,
                    IsHomeTeam       = g.Location == 'W'
                };

            var fromLoser = from g in _context.Game
                where g.Year == year
                join t   in _context.Team on g.LoserId  equals t.TeamID
                join opp in _context.Team on g.WinnerId equals opp.TeamID
                select new GameParticipant
                {
                    TeamId           = g.LoserId,
                    TeamDivision     = t.Division,
                    OpponentId       = g.WinnerId,
                    OpponentDivision = opp.Division,
                    TeamPoints       = g.LPoints,
                    OpponentPoints   = g.WPoints,
                    Location         = g.Location,
                    IsHomeTeam       = g.Location == 'L'
                };

            return await fromWinner.Union(fromLoser).ToListAsync(token);
        }
    }
}
