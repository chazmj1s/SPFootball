using SaturdayPulse.Models;

namespace SaturdayPulse.Contracts
{
    /// <summary>
    /// Data access for the WeeklyRankings table.
    /// Moved out of ILookupRepository to own its domain fully.
    /// </summary>
    public interface IWeeklyRankingsRepository
    {
        /// <summary>
        /// Returns all WeeklyRankings rows for a specific year snapshot.
        /// Used by WeeklyRankingsService (step 14) and the projections backfill.
        /// </summary>
        Task<List<WeeklyRanking>> GetByYearAsync(int year, CancellationToken token = default);

        /// <summary>
        /// Returns all WeeklyRankings rows for a specific year/week snapshot.
        /// Used by WeeklyRankingsService (step 14) and the projections backfill.
        /// </summary>
        Task<List<WeeklyRanking>> GetByYearAndWeekAsync(int year, int week, CancellationToken token = default);

        /// <summary>
        /// Returns all distinct (Year, Week) pairs that have WeeklyRankings rows,
        /// ordered chronologically. Used by the projections backfill to discover
        /// which snapshots exist.
        /// </summary>
        Task<List<(int Year, int Week)>> GetDistinctYearWeeksAsync(CancellationToken token = default);

        /// <summary>
        /// Inserts a new WeeklyRanking row.
        /// Called by WeeklyRankingsService when no existing row is found for
        /// the (TeamID, Year, Week) combination.
        /// </summary>
        Task AddAsync(WeeklyRanking ranking, CancellationToken token = default);
        /// <summary>
        /// Returns all WeeklyRankings rows for a specific team and year,
        /// ordered by week. Used by the team season arc endpoint.
        /// </summary>
        Task<List<WeeklyRanking>> GetByTeamAndYearAsync(int teamId, int year, CancellationToken token = default);
        /// <summary>
        /// Returns all WeeklyRankings rows for a specific team and year and week,
        /// ordered by week. Used by the team season arc endpoint.
        /// </summary>
        Task<Dictionary<int, WeeklyRanking>> GetByTeamsAndYearAndWeekAsync(IEnumerable<int> teamIds, int year, int week, CancellationToken token = default);
    }
}
