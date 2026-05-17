using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
{
    /// <summary>
    /// Access to reference/lookup tables shared across multiple services.
    /// AvgScoreDeltas and MatchupHistories are read-only during pipeline runs.
    /// WeeklyRankings supports both read and upsert.
    /// </summary>
    public interface ILookupRepository
    {
        Task<List<AvgScoreDelta>>  GetAvgScoreDeltasAsync(CancellationToken token = default);
        Task AddAvgScoreDeltasAsync(IEnumerable<AvgScoreDelta> deltas, CancellationToken token = default);

        Task<List<MatchupHistory>> GetMatchupHistoriesAsync(CancellationToken token = default);

        /// <summary>
        /// Returns the matchup history for a specific rivalry pair.
        /// Team IDs can be passed in either order.
        /// </summary>
        Task<MatchupHistory?> GetMatchupHistoryAsync(int team1Id, int team2Id, CancellationToken token = default);

        Task<List<WeeklyRanking>> GetWeeklyRankingsAsync(int year, int week, CancellationToken token = default);
        Task AddWeeklyRankingAsync(WeeklyRanking ranking, CancellationToken token = default);

        Task ClearAvgScoreDeltasAsync(CancellationToken token = default);
        Task ClearMatchupHistoriesAsync(CancellationToken token = default);
        Task AddMatchupHistoriesAsync(IEnumerable<MatchupHistory> histories, CancellationToken token = default);
    }
}
