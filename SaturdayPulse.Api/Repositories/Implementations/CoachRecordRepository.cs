using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
{
    public class CoachRecordRepository : ICoachRecordRepository
    {
        private readonly NCAAContext _context;

        public CoachRecordRepository(NCAAContext context) => _context = context;

        public Task<List<CoachRecord>> GetByYearAsync(int year, CancellationToken token = default)
            => _context.CoachRecords
                .Where(c => c.Year == year)
                .ToListAsync(token);

        public async Task<IReadOnlyDictionary<string, bool>> GetCoachChangeByTeamAsync(
            int year, CancellationToken token = default)
        {
            var current = await GetByYearAsync(year, token);
            var prior   = await GetByYearAsync(year - 1, token);

            var priorByTeam = prior.ToDictionary(
                c => c.Team, c => c.CoachName, StringComparer.OrdinalIgnoreCase);

            return current.ToDictionary(
                c => c.Team,
                c => priorByTeam.TryGetValue(c.Team, out var priorName) && HasChanged(c.CoachName, priorName),
                StringComparer.OrdinalIgnoreCase);
        }

        // Confirmed producing false positives on exact-string comparison — real,
        // unchanged head coaches (verified against public 2025 coaching records) were
        // being flagged as turnover, most likely from whitespace/casing drift between
        // CFBD's year-over-year coaches pulls rather than an actual coaching change.
        // Missing data on either side returns "no change" rather than penalizing —
        // consistent with the caller's existing convention of not penalizing on
        // incomplete data.
        private static bool HasChanged(string? current, string? prior)
        {
            if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(prior))
                return false;

            return !string.Equals(Normalize(current), Normalize(prior), StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string name)
            => string.Join(' ', name.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        public async Task UpsertYearAsync(int year, List<CoachRecord> entries, CancellationToken token = default)
        {
            var existing = await _context.CoachRecords
                .Where(c => c.Year == year)
                .ToListAsync(token);

            if (existing.Any())
                _context.CoachRecords.RemoveRange(existing);

            // No SaveChangesAsync here — see RosterPlayerRepository for why.
            await _context.CoachRecords.AddRangeAsync(entries, token);
        }
    }
}
