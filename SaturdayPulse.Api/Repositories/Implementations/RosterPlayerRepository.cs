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

        // RosterPlayerRepository.cs — add this method

        // Suffixes CFBD embeds directly in portal FirstName/LastName fields (confirmed against
        // real data: "Emmett Mosley V", "Paris Patterson Jr.") but that may not appear the same
        // way, if at all, in the roster endpoint's name fields. Stripped from both sides before
        // comparing so a suffix mismatch doesn't silently break the match.
        private static readonly string[] NameSuffixes = ["JR", "SR", "II", "III", "IV", "V"];

        private static string NormalizeName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";

            var trimmed = name.Trim().TrimEnd('.').ToUpperInvariant();
            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length > 1 && NameSuffixes.Contains(parts[^1]))
                parts = parts[..^1];

            return string.Join(' ', parts);
        }

        public async Task<int> ApplyPortalRatingsAsync(int season, CancellationToken token = default)
        {
            // PortalEntry has no ID shared with RosterPlayer (unlike RecruitPlayer.AthleteId ==
            // RosterPlayer.PlayerId) — CFBD's portal endpoint only gives us names and teams, so
            // this join is necessarily name-based. Weaker than ApplyRecruitRatingsAsync's ID join;
            // treat unmatched/ambiguous names as a known limitation, not a bug.
            var portalEntries = (await _context.PortalEntries
                    .Where(p => p.Season == season)
                    .Where(p => p.Destination != null)
                    .Select(p => new { p.FirstName, p.LastName, p.Origin, p.Destination, p.Rating })
                    .ToListAsync(token))
                // Origin == Destination rows are portal-entry-then-withdrawal noise, not real
                // transfers — confirmed in real data. string.Equals(..., OrdinalIgnoreCase) doesn't
                // translate to SQL — same EF Core limitation already documented in
                // RosterCapacityService for Dictionary.ContainsKey (two-step: SQL filter first,
                // then filter client-side on the materialized list). Don't recombine these into
                // one query.
                .Where(p => !string.Equals(p.Origin, p.Destination, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (portalEntries.Count == 0) return 0;

            // Rating == 0 means CFBD never assigned a transfer grade (confirmed against real
            // 2021 data — redshirted/no-tape players get skipped by evaluators, not scored as
            // zero-value) — NOT a genuine zero rating. Coalesced to null here so it doesn't
            // override the RecruitRating fallback in RosterCapacityService's cascade.
            var ratingByKey = portalEntries
                .Where(p => p.Rating.HasValue && p.Rating.Value > 0)
                .GroupBy(p => (NormalizeName(p.FirstName), NormalizeName(p.LastName), p.Destination!.Trim().ToUpperInvariant()))
                // Defensive: if two same-named transfers land on the same team in the same
                // season, take the first rather than throwing — a genuine ambiguity this join
                // can't resolve with the data CFBD provides, not something to fail loudly on.
                .ToDictionary(g => g.Key, g => g.First().Rating!.Value);

            var rosterRows = await _context.RosterPlayers
                .Where(rp => rp.Season == season)
                .ToListAsync(token);

            var updated = 0;
            foreach (var rosterRow in rosterRows)
            {
                var key = (NormalizeName(rosterRow.FirstName), NormalizeName(rosterRow.LastName),
                           rosterRow.Team.Trim().ToUpperInvariant());

                if (!ratingByKey.TryGetValue(key, out var rating)) continue;
                rosterRow.TransferRating = rating;
                updated++;
            }

            // Self-saves — matches ApplyRecruitRatingsAsync's convention.
            if (updated > 0)
                await _context.SaveChangesAsync(token);

            return updated;
        }
    }
}
