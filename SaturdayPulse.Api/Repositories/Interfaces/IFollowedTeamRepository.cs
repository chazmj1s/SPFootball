using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
{
    public interface IFollowedTeamRepository
    {
        Task<List<FollowedTeam>> GetByUserIdAsync(string userId, CancellationToken token = default);
        Task<bool> IsFollowedAsync(string userId, int teamId, CancellationToken token = default);
        Task FollowAsync(string userId, int teamId, CancellationToken token = default);
        Task UnfollowAsync(string userId, int teamId, CancellationToken token = default);
    }
}
