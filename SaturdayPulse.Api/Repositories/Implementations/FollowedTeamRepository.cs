using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
{
    public class FollowedTeamRepository : IFollowedTeamRepository
    {
        private readonly NCAAContext _context;
        public FollowedTeamRepository(NCAAContext context) => _context = context;

        public Task<List<FollowedTeam>> GetByUserIdAsync(string userId, CancellationToken token = default)
            => _context.FollowedTeams.Where(f => f.UserId == userId).ToListAsync(token);

        public Task<bool> IsFollowedAsync(string userId, int teamId, CancellationToken token = default)
            => _context.FollowedTeams.AnyAsync(f => f.UserId == userId && f.TeamId == teamId, token);

        public async Task FollowAsync(string userId, int teamId, CancellationToken token = default)
        {
            var exists = await _context.FollowedTeams
                .AnyAsync(f => f.UserId == userId && f.TeamId == teamId, token);

            if (exists) return; // idempotent — following an already-followed team is a no-op, not an error

            await _context.FollowedTeams.AddAsync(new FollowedTeam
            {
                UserId = userId,
                TeamId = teamId,
                FollowedAt = DateTime.UtcNow,
                IsSynced = false
            }, token);
        }

        public async Task UnfollowAsync(string userId, int teamId, CancellationToken token = default)
        {
            var existing = await _context.FollowedTeams
                .FirstOrDefaultAsync(f => f.UserId == userId && f.TeamId == teamId, token);

            if (existing != null)
                _context.FollowedTeams.Remove(existing);
        }
    }
}
