using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
{
    public class FollowedGameRepository : IFollowedGameRepository
    {
        private readonly NCAAContext _context;
        public FollowedGameRepository(NCAAContext context) => _context = context;

        // Canonical ordering — matches PersonalGameService.Key()'s
        // Math.Min/Math.Max approach, so 251/201 and 201/251 always
        // resolve to the same stored row.
        private static (int lo, int hi) Canonical(int team1Id, int team2Id)
            => team1Id <= team2Id ? (team1Id, team2Id) : (team2Id, team1Id);

        public Task<List<FollowedGame>> GetByUserIdAsync(string userId, CancellationToken token = default)
            => _context.FollowedGames.Where(f => f.UserId == userId).ToListAsync(token);

        public Task<bool> IsFollowedAsync(string userId, int team1Id, int team2Id, CancellationToken token = default)
        {
            var (lo, hi) = Canonical(team1Id, team2Id);
            return _context.FollowedGames.AnyAsync(
                f => f.UserId == userId && f.Team1Id == lo && f.Team2Id == hi, token);
        }

        public async Task FollowAsync(string userId, int team1Id, int team2Id, CancellationToken token = default)
        {
            var (lo, hi) = Canonical(team1Id, team2Id);

            var exists = await _context.FollowedGames
                .AnyAsync(f => f.UserId == userId && f.Team1Id == lo && f.Team2Id == hi, token);

            if (exists) return; // idempotent, same as FollowedTeam

            await _context.FollowedGames.AddAsync(new FollowedGame
            {
                UserId = userId,
                Team1Id = lo,
                Team2Id = hi,
                FollowedAt = DateTime.UtcNow,
                IsSynced = false
            }, token);
        }

        public async Task UnfollowAsync(string userId, int team1Id, int team2Id, CancellationToken token = default)
        {
            var (lo, hi) = Canonical(team1Id, team2Id);

            var existing = await _context.FollowedGames
                .FirstOrDefaultAsync(f => f.UserId == userId && f.Team1Id == lo && f.Team2Id == hi, token);

            if (existing != null)
                _context.FollowedGames.Remove(existing);
        }
    }
}
