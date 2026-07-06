using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
{
    public class RecruitPlayerRepository : IRecruitPlayerRepository
    {
        private readonly NCAAContext _context;

        public RecruitPlayerRepository(NCAAContext context) => _context = context;

        public Task<List<RecruitPlayer>> GetByYearAsync(int year, CancellationToken token = default)
            => _context.RecruitPlayers
                .Where(r => r.Year == year)
                .ToListAsync(token);

        public async Task UpsertYearAsync(int year, List<RecruitPlayer> entries, CancellationToken token = default)
        {
            // ExecuteDeleteAsync issues a direct SQL DELETE without loading rows into the
            // change tracker — same reasoning as RosterPlayerRepository/CoachRecordRepository:
            // a reload of the same year would otherwise collide with the just-deleted rows
            // under the same Id key.
            await _context.RecruitPlayers
                .Where(r => r.Year == year)
                .ExecuteDeleteAsync(token);

            // Defensive dedup on Id, same as the other Upsert* methods.
            var deduped = entries
                .GroupBy(e => e.Id)
                .Select(g => g.First())
                .ToList();

            // No SaveChangesAsync here — UoW's job, called once by the calling service.
            await _context.RecruitPlayers.AddRangeAsync(deduped, token);
        }
    }
}
