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

        public Task<CoachRecord?> GetByTeamAndYearAsync(string team, int year, CancellationToken token = default)
            => _context.CoachRecords
                .FirstOrDefaultAsync(c => c.Team == team && c.Year == year, token);

        public Task<List<int>> GetDistinctYearsAsync(CancellationToken token = default)
            => _context.CoachRecords
                .Select(c => c.Year)
                .Distinct()
                .OrderBy(y => y)
                .ToListAsync(token);

        public async Task UpsertYearAsync(int year, List<CoachRecord> entries, CancellationToken token = default)
        {
            var existing = await _context.CoachRecords
                .Where(c => c.Year == year)
                .ToListAsync(token);

            if (existing.Any())
                _context.CoachRecords.RemoveRange(existing);

            await _context.CoachRecords.AddRangeAsync(entries, token);
            await _context.SaveChangesAsync(token);
        }
    }
}
