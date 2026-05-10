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

        public Task<List<WeeklyRanking>> GetWeeklyRankingsAsync(int year, int week, CancellationToken token = default)
            => _context.WeeklyRankings
                .Where(wr => wr.Year == year && wr.Week == week)
                .ToListAsync(token);
    }
}
