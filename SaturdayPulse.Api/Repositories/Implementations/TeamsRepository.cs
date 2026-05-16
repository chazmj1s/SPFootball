using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
{
    public class TeamsRepository : ITeamsRepository
    {
        private readonly NCAAContext _context;
        public TeamsRepository(NCAAContext context) => _context = context;

        public Task<List<Teams>> GetAllAsync(CancellationToken token = default)
            => _context.Teams.OrderBy(t => t.TeamName).ToListAsync(token);

        public Task<Teams?> GetByTeamIdAsync(int teamId, CancellationToken token = default)
            => _context.Teams.FirstOrDefaultAsync(t => t.TeamId == teamId, token);

        public Task<Dictionary<int, Teams>> GetDictionaryByTeamIdAsync(CancellationToken token = default)
            => _context.Teams.ToDictionaryAsync(t => t.TeamId, token);

        public Task<Dictionary<string, Teams>> GetDictionaryByNameAsync(CancellationToken token = default)
            => _context.Teams.ToDictionaryAsync(t => t.TeamName, token);

        public async Task UpsertAsync(Teams team, CancellationToken token = default)
        {
            var existing = await _context.Teams
                .FirstOrDefaultAsync(t => t.TeamId == team.TeamId, token);

            if (existing == null)
                _context.Teams.Add(team);
            else
            {
                existing.TeamName     = team.TeamName;
                existing.Mascot       = team.Mascot;
                existing.Abbreviation = team.Abbreviation;
                existing.Alias        = team.Alias;
                existing.Division     = team.Division;
                existing.ConferenceId = team.ConferenceId;
                existing.ShortName    = team.ShortName;
            }
        }

        public async Task UpsertRangeAsync(IEnumerable<Teams> teams, CancellationToken token = default)
        {
            var incoming    = teams.ToList();
            var incomingIds = incoming.Select(t => t.TeamId).ToHashSet();

            var existing = await _context.Teams
                .Where(t => incomingIds.Contains(t.TeamId))
                .ToDictionaryAsync(t => t.TeamId, token);

            foreach (var team in incoming)
            {
                if (existing.TryGetValue(team.TeamId, out var dbTeam))
                {
                    dbTeam.TeamName     = team.TeamName;
                    dbTeam.Mascot       = team.Mascot;
                    dbTeam.Abbreviation = team.Abbreviation;
                    dbTeam.Alias        = team.Alias;
                    dbTeam.Division     = team.Division;
                    dbTeam.ConferenceId = team.ConferenceId;
                    dbTeam.ShortName    = team.ShortName;
                }
                else
                    _context.Teams.Add(team);
            }
        }
    }
}
