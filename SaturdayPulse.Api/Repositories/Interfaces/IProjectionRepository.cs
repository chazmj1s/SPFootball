using SaturdayPulse.Models;

namespace SaturdayPulse.Contracts
{
    /// <summary>
    /// Data access for persisted game projections.
    /// All writes go through UpsertManyAsync — existing rows for the same
    /// (GameId, Year, Week) are updated in place; new rows are inserted.
    /// </summary>
    public interface IProjectionRepository
    {
        Task<List<Projection>> GetByYearAsync(int year, CancellationToken token = default);

        /// <summary>
        /// Returns all projections for a given year/week combination.
        /// Used by schedule and standings endpoints to read the active snapshot.
        /// </summary>
        Task<List<Projection>> GetByYearAndWeekAsync(
            int year, int week, CancellationToken token = default);

        /// <summary>
        /// Upserts a batch of projections.
        /// Matches on the unique key (GameId, Year, Week).
        /// </summary>
        Task UpsertManyAsync(
            IEnumerable<Projection> projections, CancellationToken token = default);
    }
}
