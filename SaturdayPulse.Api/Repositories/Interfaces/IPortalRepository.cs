using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
{
    /// <summary>
    /// Repository for transfer portal entries.
    /// </summary>
    public interface IPortalRepository
    {
        /// <summary>
        /// Returns all portal entries for a given season.
        /// </summary>
        Task<List<PortalEntry>> GetBySeasonAsync(int season, CancellationToken token = default);

        /// <summary>
        /// Returns all distinct seasons available in the table.
        /// </summary>
        Task<List<int>> GetDistinctSeasonsAsync(CancellationToken token = default);

        /// <summary>
        /// Deletes all portal entries for the given season and inserts the new batch.
        /// Safe to call multiple times — replaces existing data.
        /// </summary>
        Task UpsertSeasonAsync(int season, List<PortalEntry> entries, CancellationToken token = default);

    }
}
