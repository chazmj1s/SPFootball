using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
{
    public class GamesRepository : IGamesRepository
    {
        private readonly NCAAContext _context;
        public GamesRepository(NCAAContext context) => _context = context;

        public async Task<List<int>> GetUnplayedYearDistinct(int year, CancellationToken token = default)
            => await _context.Games
                .Where(g => g.Year >= year && g.HomePoints == null && g.AwayPoints == null)
                .Select(wr => wr.Year)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(token);

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
        public async Task<List<Games>> GetRivalryHistoryAsync(int team1Id, int team2Id, int cutoffYear, CancellationToken token = default)
        {
            return await _context.Games
                .Where(g => g.Year >= cutoffYear &&
                            ((g.HomeId == team1Id && g.AwayId == team2Id) ||
                             (g.HomeId == team2Id && g.AwayId == team1Id)))
                .OrderByDescending(g => g.Year)
                .ThenBy(g => g.Week)
                .ToListAsync(token);
        }

        public Task<List<Games>> GetGamesSinceYearAsync(int fromYear, CancellationToken token = default)
            => _context.Games
                .Where(g => g.Year >= fromYear)
                .ToListAsync(token);

        public Task<List<Games>> GetPlayedGamesSinceYearAsync(int fromYear, CancellationToken token = default)
            => _context.Games
                .Where(g => g.Year >= fromYear &&
                            ((g.HomePoints ?? 0) > 0 || (g.AwayPoints ?? 0) > 0))
                .ToListAsync(token);

        public Task<List<Games>> GetPlayedGamesByYearAndWeekAsync(int year, int week, CancellationToken token = default)
            => _context.Games
                .Where(g => g.Year == year &&
                            g.Week <= week &&
                            ((g.HomePoints ?? 0) > 0 || (g.AwayPoints ?? 0) > 0))
                .ToListAsync(token);

        public async Task<List<PlayedWeekDto>> GetPlayedWeeksByYearAsync(
            int year,
            CancellationToken token = default)
        {
            var databaseData = await _context.Games
                .Where(g => g.Year == year && g.HomePoints != 0 && g.AwayPoints != 0)
                .Select(g => new { g.Week, g.GameDate })
                .Distinct()
                .OrderBy(w => w.Week)
                .ToListAsync(token);

            return databaseData
                .Select(w => new PlayedWeekDto((int)w.Week, w.GameDate))
                .ToList();
        }

        public async Task<List<GameParticipant>> GetGameParticipantsAsync(
            int year, CancellationToken token = default)
        {
            var fromHome = from g in _context.Games
                where g.Year == year
                join t   in _context.Teams on g.HomeId equals t.TeamId
                join opp in _context.Teams on g.AwayId equals opp.TeamId
                select new GameParticipant
                {
                    TeamId           = g.HomeId ?? 0,
                    TeamDivision     = t.Division,
                    OpponentId       = g.AwayId ?? 0,
                    OpponentDivision = opp.Division,
                    TeamPoints       = g.HomePoints ?? 0,
                    OpponentPoints   = g.AwayPoints ?? 0,
                    Location         = g.NeutralSite == true ? 'N' : 'H',
                    IsHomeTeam       = true,
                    Week             = g.Week
                };

            var fromAway = from g in _context.Games
                where g.Year == year
                join t   in _context.Teams on g.AwayId equals t.TeamId
                join opp in _context.Teams on g.HomeId equals opp.TeamId
                select new GameParticipant
                {
                    TeamId           = g.AwayId ?? 0,
                    TeamDivision     = t.Division,
                    OpponentId       = g.HomeId ?? 0,
                    OpponentDivision = opp.Division,
                    TeamPoints       = g.AwayPoints ?? 0,
                    OpponentPoints   = g.HomePoints ?? 0,
                    Location         = g.NeutralSite == true ? 'N' : 'A',
                    IsHomeTeam       = false,
                    Week = g.Week
                };

            return await fromHome.Union(fromAway).ToListAsync(token);
        }

        public Task<List<Games>> GetPostSeasonByYear(int year, CancellationToken token = default)
            => _context.Games
                .Where(g => g.Year == year &&
                            new List<string> { "postseason", "playoff" }.Contains(g.SeasonType))
                .OrderBy(g => g.SeasonType)
                .ThenBy(g => g.Week)
                .ToListAsync(token);
        
        public async Task<List<Games>> GetByIds(List<int> gameIds, CancellationToken token = default)
            => await _context.Games
                        .Where(g => gameIds.Contains(g.GameId))
                        .ToListAsync(token);


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
