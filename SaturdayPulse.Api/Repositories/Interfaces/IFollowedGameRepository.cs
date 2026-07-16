using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
{
    public interface IFollowedGameRepository
    {
        Task<List<FollowedGame>> GetByUserIdAsync(string userId, CancellationToken token = default);
        Task<bool> IsFollowedAsync(string userId, int team1Id, int team2Id, CancellationToken token = default);
        Task FollowAsync(string userId, int team1Id, int team2Id, CancellationToken token = default);
        Task UnfollowAsync(string userId, int team1Id, int team2Id, CancellationToken token = default);
    }
}
