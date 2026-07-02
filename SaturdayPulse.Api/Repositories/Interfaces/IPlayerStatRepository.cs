using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
{
    /// <summary>
    /// Repository for player season stat lines, used to compute production shares
    /// for departed players in the Roster Capacity Modifier.
    /// </summary>
    public interface IPlayerStatRepository
    {
        /// <summary>
        /// Returns all stat rows for a given season (across all teams).
        /// </summary>
        Task<List<PlayerStat>> GetBySeasonAsync(int season, CancellationToken token = default);

        /// <summary>
        /// Returns all stat rows for a single team in a given season.
        /// </summary>
        Task<List<PlayerStat>> GetByTeamAndSeasonAsync(string team, int season, CancellationToken token = default);

        /// <summary>
        /// Returns all stat rows for a single player in a given season.
        /// </summary>
        Task<List<PlayerStat>> GetByPlayerAndSeasonAsync(string playerId, int season, CancellationToken token = default);

        /// <summary>
        /// Returns all distinct seasons available in the table.
        /// </summary>
        Task<List<int>> GetDistinctSeasonsAsync(CancellationToken token = default);

        /// <summary>
        /// Deletes all stat rows for the given season and inserts the new batch.
        /// Safe to call multiple times — replaces existing data.
        /// </summary>
        Task UpsertSeasonAsync(int season, List<PlayerStat> entries, CancellationToken token = default);
    }
}
