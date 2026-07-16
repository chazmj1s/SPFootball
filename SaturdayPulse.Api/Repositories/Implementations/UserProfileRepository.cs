using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
{
    public class UserProfileRepository : IUserProfileRepository
    {
        private readonly NCAAContext _context;
        public UserProfileRepository(NCAAContext context) => _context = context;

        public Task<UserProfile?> GetByUserIdAsync(string userId, CancellationToken token = default)
            => _context.UserProfiles.FirstOrDefaultAsync(u => u.UserId == userId, token);

        public Task<UserProfile?> GetByHandleAsync(string handle, CancellationToken token = default)
            // Handle column is NOCASE-collated, so this comparison is
            // case-insensitive at the DB level already — no ToLower() needed.
            => _context.UserProfiles.FirstOrDefaultAsync(u => u.Handle == handle, token);

        public async Task<bool> IsHandleAvailableAsync(string handle, string? excludingUserId = null, CancellationToken token = default)
        {
            var query = _context.UserProfiles.Where(u => u.Handle == handle);

            if (excludingUserId != null)
                query = query.Where(u => u.UserId != excludingUserId);

            return !await query.AnyAsync(token);
        }

        public async Task CreateAsync(UserProfile profile, CancellationToken token = default)
        {
            profile.CreatedAt = DateTime.UtcNow;
            profile.UpdatedAt = DateTime.UtcNow;
            await _context.UserProfiles.AddAsync(profile, token);
        }

        public async Task UpdateHandleAsync(string userId, string newHandle, CancellationToken token = default)
        {
            var profile = await _context.UserProfiles
                .FirstOrDefaultAsync(u => u.UserId == userId, token);

            if (profile == null) return;

            profile.Handle = newHandle;
            profile.HandleChangedAt = DateTime.UtcNow;
            profile.UpdatedAt = DateTime.UtcNow;
            profile.IsSynced = false;
        }

        public async Task UpdatePrimaryTeamAsync(string userId, int? teamId, CancellationToken token = default)
        {
            var profile = await _context.UserProfiles
                .FirstOrDefaultAsync(u => u.UserId == userId, token);

            if (profile == null) return;

            profile.PrimaryTeamId = teamId;
            profile.UpdatedAt = DateTime.UtcNow;
            profile.IsSynced = false;
        }
    }
}
