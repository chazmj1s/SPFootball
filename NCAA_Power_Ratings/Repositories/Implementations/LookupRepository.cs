using Microsoft.EntityFrameworkCore;
using NCAA_Power_Ratings.Data;
using NCAA_Power_Ratings.Models;
using NCAA_Power_Ratings.Repositories.Interfaces;

namespace NCAA_Power_Ratings.Repositories.Implementations
{
    public class LookupRepository : ILookupRepository
    {
        private readonly NCAAContext _context;

        public LookupRepository(NCAAContext context) => _context = context;

        public Task<List<AvgScoreDelta>> GetAvgScoreDeltasAsync(CancellationToken token = default)
            => _context.AvgScoreDeltas.ToListAsync(token);

        public Task<List<MatchupHistory>> GetMatchupHistoriesAsync(CancellationToken token = default)
            => _context.MatchupHistories.ToListAsync(token);

        public Task<MatchupHistory?> GetMatchupHistoryAsync(
            int team1Id, int team2Id, CancellationToken token = default)
            => _context.MatchupHistories.FirstOrDefaultAsync(m =>
                (m.Team1Id == team1Id && m.Team2Id == team2Id) ||
                (m.Team1Id == team2Id && m.Team2Id == team1Id), token);

        public Task<List<WeeklyRanking>> GetWeeklyRankingsAsync(
            int year, int week, CancellationToken token = default)
            => _context.WeeklyRankings
                .Where(wr => wr.Year == year && wr.Week == week)
                .ToListAsync(token);

        public async Task AddWeeklyRankingAsync(
            WeeklyRanking ranking, CancellationToken token = default)
            => await _context.WeeklyRankings.AddAsync(ranking, token);

        public Task ClearAvgScoreDeltasAsync(CancellationToken token = default)
            => _context.Database.ExecuteSqlRawAsync("DELETE FROM AvgScoreDeltas", token);

        public Task ClearMatchupHistoriesAsync(CancellationToken token = default)
            => _context.Database.ExecuteSqlRawAsync("DELETE FROM MatchupHistory", token);

        public Task AddMatchupHistoriesAsync(
            IEnumerable<MatchupHistory> histories, CancellationToken token = default)
            => _context.MatchupHistories.AddRangeAsync(histories, token);
    }
}
