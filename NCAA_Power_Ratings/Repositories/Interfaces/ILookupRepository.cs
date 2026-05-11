using NCAA_Power_Ratings.Models;

namespace NCAA_Power_Ratings.Repositories.Interfaces
{
    /// <summary>
    /// Access to reference/lookup tables shared across multiple services.
    /// AvgScoreDeltas and MatchupHistories are read-only during pipeline runs.
    /// WeeklyRankings supports both read and upsert.
    /// </summary>
    public interface ILookupRepository
    {
        Task<List<AvgScoreDelta>>  GetAvgScoreDeltasAsync(CancellationToken token = default);
        Task<List<MatchupHistory>> GetMatchupHistoriesAsync(CancellationToken token = default);
        Task<List<WeeklyRanking>>  GetWeeklyRankingsAsync(int year, int week, CancellationToken token = default);
        Task                       AddWeeklyRankingAsync(WeeklyRanking ranking, CancellationToken token = default);
    }
}
