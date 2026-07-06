using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
{
    public class RosterPlayerRepository : IRosterPlayerRepository
    {
        private readonly NCAAContext _context;

        public RosterPlayerRepository(NCAAContext context) => _context = context;

        public Task<List<RosterPlayer>> GetBySeasonAsync(int season, CancellationToken token = default)
            => _context.RosterPlayers
                .Where(r => r.Season == season)
                .ToListAsync(token);

        public Task<List<RosterPlayer>> GetByTeamAndSeasonAsync(string team, int season, CancellationToken token = default)
            => _context.RosterPlayers
                .Where(r => r.Team == team && r.Season == season)
                .ToListAsync(token);

        public Task<List<int>> GetDistinctSeasonsAsync(CancellationToken token = default)
            => _context.RosterPlayers
                .Select(r => r.Season)
                .Distinct()
                .OrderBy(s => s)
                .ToListAsync(token);

        public async Task UpsertSeasonAsync(int season, List<RosterPlayer> entries, CancellationToken token = default)
        {
            // ExecuteDeleteAsync issues a direct SQL DELETE without loading rows into the
            // change tracker. RemoveRange+AddRange doesn't work here the way it does in
            // PortalRepository: PortalEntry's Id is a surrogate auto-increment key, so newly
            // added entities (Id=0) never collide with tracked-as-Deleted existing rows.
            // RosterPlayer's key is the real (PlayerId, Season) business value, so a reload
            // of the same season means the new entities share a key with the rows just marked
            // for deletion — EF won't track two different instances under the same key at
            // once, which is exactly the "cannot be tracked" exception this replaces.
            await _context.RosterPlayers
                .Where(r => r.Season == season)
                .ExecuteDeleteAsync(token);

            // Defensive dedup: guards against AddRangeAsync hitting a tracking conflict if
            // CFBD ever returns a literal exact duplicate row (same PlayerId+Team+Season twice).
            var deduped = entries
                .GroupBy(e => (e.PlayerId, e.Team, e.Season))
                .Select(g => g.First())
                .ToList();

            // No SaveChangesAsync here — see IUnitOfWork usage in GameDataService for why.
            await _context.RosterPlayers.AddRangeAsync(deduped, token);
        }

        public async Task<int> ApplyRecruitRatingsAsync(int season, CancellationToken token = default)
        {
            // Only recruits with an AthleteId can join — the ~38% with no AthleteId yet
            // simply won't match anything below, which is correct: they fall through to
            // the 0.70 unrated floor at compute time rather than raising an error here.
            var recruitRatings = await _context.RecruitPlayers
                .Where(r => r.Year == season && r.AthleteId != null)
                .Select(r => new { r.AthleteId, r.Rating })
                .ToListAsync(token);

            if (recruitRatings.Count == 0) return 0;

            var ratingByAthleteId = recruitRatings
                .GroupBy(r => r.AthleteId!)
                .ToDictionary(g => g.Key, g => g.First().Rating);

            var rosterRows = await _context.RosterPlayers
                .Where(rp => rp.Season == season)
                .ToListAsync(token);

            var updated = 0;
            foreach (var rosterRow in rosterRows)
            {
                if (!ratingByAthleteId.TryGetValue(rosterRow.PlayerId, out var rating)) continue;
                rosterRow.RecruitRating = rating;
                updated++;
            }

            // Self-saves — matches the ComputeYAsync convention (called directly from
            // DeveloperService with no follow-up SaveChangesAsync, unlike the Upsert* methods).
            if (updated > 0)
                await _context.SaveChangesAsync(token);

            return updated;
        }
    }
}
