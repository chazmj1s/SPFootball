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
    }
}
