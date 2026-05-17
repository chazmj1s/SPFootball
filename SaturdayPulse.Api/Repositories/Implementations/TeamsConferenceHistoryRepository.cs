using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
{
    public class TeamsConferenceHistoryRepository : ITeamsConferenceHistoryRepository
    {
        private readonly NCAAContext _context;
        public TeamsConferenceHistoryRepository(NCAAContext context) => _context = context;

        public Task<List<TeamsConferenceHistory>> GetByTeamIdAsync(int teamId, CancellationToken token = default)
            => _context.TeamsConferenceHistory
                .Where(t => t.TeamId == teamId)
                .OrderBy(t => t.StartYear)
                .ToListAsync(token);

        public Task<TeamsConferenceHistory?> GetByTeamAndYearAsync(int teamId, int year, CancellationToken token = default)
            => _context.TeamsConferenceHistory
                .FirstOrDefaultAsync(t => t.TeamId == teamId
                    && t.StartYear <= year
                    && (t.EndYear == null || t.EndYear >= year), token);

        public Task<Dictionary<int, int>> GetConferenceIdsByYearAsync(int year, CancellationToken token = default)
            => _context.TeamsConferenceHistory
                .Where(t => t.StartYear <= year && (t.EndYear == null || t.EndYear >= year))
                .ToDictionaryAsync(t => t.TeamId, t => t.ConferenceId, token);

        public async Task AddAsync(TeamsConferenceHistory record, CancellationToken token = default)
        {
            _context.TeamsConferenceHistory.Add(record);
            await _context.SaveChangesAsync(token);
        }

        public async Task UpdateAsync(TeamsConferenceHistory record, CancellationToken token = default)
        {
            _context.TeamsConferenceHistory.Update(record);
            await _context.SaveChangesAsync(token);
        }
    }
}