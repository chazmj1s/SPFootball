using NCAA_Power_Ratings.Models;

namespace NCAA_Power_Ratings.Repositories.Interfaces
{
    /// <summary>
    /// Read-only access to reference/lookup tables that are loaded in bulk
    /// and shared across multiple services in the same operation.
    ///
    /// AvgScoreDeltas and MatchupHistories are never mutated during a
    /// prediction or metrics pass — they belong here rather than on the
    /// write-capable repositories.
    /// </summary>
    public interface ILookupRepository
    {
        Task<List<AvgScoreDelta>>  GetAvgScoreDeltasAsync(CancellationToken token = default);
        Task<List<MatchupHistory>> GetMatchupHistoriesAsync(CancellationToken token = default);
        Task<List<WeeklyRanking>>  GetWeeklyRankingsAsync(int year, int week, CancellationToken token = default);
    }
}
