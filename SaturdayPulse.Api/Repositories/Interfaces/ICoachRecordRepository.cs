using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
{
    /// <summary>
    /// Repository for flattened (Team, Year) coach records, used to detect head-coach
    /// turnover for the coaching-penalty step in the Roster Capacity Modifier.
    /// </summary>
    public interface ICoachRecordRepository
    {
        /// <summary>
        /// Returns all coach records for a given year (across all teams).
        /// </summary>
        Task<List<CoachRecord>> GetByYearAsync(int year, CancellationToken token = default);

        /// <summary>
        /// Returns the coach record for a single team in a given year, or null if not found.
        /// </summary>
        Task<CoachRecord?> GetByTeamAndYearAsync(string team, int year, CancellationToken token = default);

        /// <summary>
        /// Returns all distinct years available in the table.
        /// </summary>
        Task<List<int>> GetDistinctYearsAsync(CancellationToken token = default);

        /// <summary>
        /// Deletes all coach records for the given year and inserts the new batch.
        /// Safe to call multiple times — replaces existing data.
        /// </summary>
        Task UpsertYearAsync(int year, List<CoachRecord> entries, CancellationToken token = default);
    }
}
