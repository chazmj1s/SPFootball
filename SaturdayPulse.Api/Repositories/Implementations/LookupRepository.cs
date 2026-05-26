using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
{
    public class LookupRepository : ILookupRepository
    {
        private readonly NCAAContext _context;

        public LookupRepository(NCAAContext context) => _context = context;

        public Task<List<AvgScoreDelta>> GetAvgScoreDeltasAsync(CancellationToken token = default)
            => _context.AvgScoreDeltas.ToListAsync(token);

        public Task AddAvgScoreDeltasAsync(
            IEnumerable<AvgScoreDelta> deltas, CancellationToken token = default)
            => _context.AvgScoreDeltas.AddRangeAsync(deltas, token);

        public Task<List<MatchupHistory>> GetMatchupHistoriesAsync(CancellationToken token = default)
            => _context.MatchupHistories.ToListAsync(token);

        public Task<MatchupHistory?> GetMatchupHistoryAsync(
            int team1Id, int team2Id, CancellationToken token = default)
            => _context.MatchupHistories.FirstOrDefaultAsync(m =>
                (m.Team1Id == team1Id && m.Team2Id == team2Id) ||
                (m.Team1Id == team2Id && m.Team2Id == team1Id), token);

        public Task ClearAvgScoreDeltasAsync(CancellationToken token = default)
            => _context.Database.ExecuteSqlRawAsync("DELETE FROM AvgScoreDeltas", token);

        public Task ClearMatchupHistoriesAsync(CancellationToken token = default)
            => _context.Database.ExecuteSqlRawAsync("DELETE FROM MatchupHistory", token);

        public Task AddMatchupHistoriesAsync(
            IEnumerable<MatchupHistory> histories, CancellationToken token = default)
            => _context.MatchupHistories.AddRangeAsync(histories, token);

        public Task<List<AvgScoreDifferential>> GetAvgScoreDifferentialsAsync(CancellationToken token = default)
            => _context.AvgScoreDifferentials.ToListAsync(token);

        public Task AddAvgScoreDifferentialsAsync(IEnumerable<AvgScoreDifferential> differentials,CancellationToken token = default)
            => _context.AvgScoreDifferentials.AddRangeAsync(differentials, token);

        public Task ClearAvgScoreDifferentialsAsync(CancellationToken token = default)
            => _context.Database.ExecuteSqlRawAsync("DELETE FROM AvgScoreDifferentials",token);
    }
}
