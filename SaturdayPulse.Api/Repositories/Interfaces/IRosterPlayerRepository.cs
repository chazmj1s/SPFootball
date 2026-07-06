using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
{
    /// <summary>
    /// Repository for roster player entries (both current-year and prior-year snapshots).
    /// </summary>
    public interface IRosterPlayerRepository
    {
        /// <summary>
        /// Returns all roster rows for a given season (across all teams).
        /// </summary>
        Task<List<RosterPlayer>> GetBySeasonAsync(int season, CancellationToken token = default);

        /// <summary>
        /// Returns roster rows for a single team in a given season.
        /// </summary>
        Task<List<RosterPlayer>> GetByTeamAndSeasonAsync(string team, int season, CancellationToken token = default);

        /// <summary>
        /// Returns all distinct seasons available in the table.
        /// </summary>
        Task<List<int>> GetDistinctSeasonsAsync(CancellationToken token = default);

        /// <summary>
        /// Deletes all roster rows for the given season and inserts the new batch.
        /// Safe to call multiple times — replaces existing data. Note: unlike portal entries,
        /// a full roster load needs to be called once per season (T and T-1 are separate calls,
        /// each with entries already tagged to the correct Season before being passed in).
        /// </summary>
        Task UpsertSeasonAsync(int season, List<RosterPlayer> entries, CancellationToken token = default);

        /// <summary>
        /// Joins RecruitPlayers (Year == season) to RosterPlayers (Season == season) on
        /// AthleteId == PlayerId and writes RecruitRating for every match. Recruits with no
        /// AthleteId, or roster players with no matching recruit row, are left alone — they
        /// keep whatever RecruitRating they already had (typically null, which falls back to
        /// the 0.70 unrated floor at compute time). Pure DB computation — self-saves, matching
        /// the ComputeYAsync convention (no external SaveChangesAsync call needed after this).
        /// Returns the number of RosterPlayer rows updated.
        /// </summary>
        Task<int> ApplyRecruitRatingsAsync(int season, CancellationToken token = default);
    }
}
