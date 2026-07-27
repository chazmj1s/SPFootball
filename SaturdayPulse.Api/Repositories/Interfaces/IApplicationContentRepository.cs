using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
{
    public interface IApplicationContentRepository
    {
        /// <summary>The single content row, or null if it's never been saved yet.</summary>
        Task<ApplicationContent?> GetAsync(CancellationToken token = default);

        /// <summary>Inserts the first-ever row. Only called when GetAsync returns null.</summary>
        Task CreateAsync(ApplicationContent content, CancellationToken token = default);

        /// <summary>
        /// Updates the single existing row in place - same "fetch inside the
        /// repo, mutate tracked entity" pattern as UserProfileRepository's
        /// UpdateHandleAsync/UpdatePrimaryTeamAsync. No-ops if no row exists
        /// (caller should have used CreateAsync for the first save).
        /// </summary>
        Task UpdateAsync(int version, string contentJson, DateTime lastModifiedUtc, CancellationToken token = default);
    }
}
