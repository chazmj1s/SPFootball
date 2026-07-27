using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
{
    public interface IUserProfileRepository
    {
        Task<UserProfile?> GetByUserIdAsync(string userId, CancellationToken token = default);
        Task<UserProfile?> GetByHandleAsync(string handle, CancellationToken token = default);
        Task<bool> IsHandleAvailableAsync(string handle, string? excludingUserId = null, CancellationToken token = default);
        Task CreateAsync(UserProfile profile, CancellationToken token = default);
        Task UpdateHandleAsync(string userId, string newHandle, CancellationToken token = default);
        Task UpdatePrimaryTeamAsync(string userId, int? teamId, CancellationToken token = default);

        /// <summary>All user profiles, ordered by Handle. Backs the admin console's
        /// Users page - fine as an unpaginated full-table read at current scale
        /// (pre-beta, single admin); revisit if the user count grows large.</summary>
        Task<List<UserProfile>> GetAllAsync(CancellationToken token = default);
    }
}
