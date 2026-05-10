using Microsoft.EntityFrameworkCore;
using NCAA_Power_Ratings.Data;
using NCAA_Power_Ratings.Models;
using NCAA_Power_Ratings.Repositories.Interfaces;

namespace NCAA_Power_Ratings.Repositories.Implementations
{
    public class TeamRepository : ITeamRepository
    {
        private readonly NCAAContext _context;

        public TeamRepository(NCAAContext context) => _context = context;

        public Task<Team?> GetByIdAsync(int teamId, CancellationToken token = default)
            => _context.Team.FirstOrDefaultAsync(t => t.TeamID == teamId, token);

        public Task<Team?> GetByNameAsync(string teamName, CancellationToken token = default)
            => _context.Team.FirstOrDefaultAsync(t => t.TeamName == teamName, token);

        public Task<List<Team>> GetAllAsync(CancellationToken token = default)
            => _context.Team.OrderBy(t => t.TeamName).ToListAsync(token);

        public Task<List<Team>> GetFbsTeamsAsync(CancellationToken token = default)
            => _context.Team
                .Where(t => t.Division == "FBS")
                .OrderBy(t => t.TeamName)
                .ToListAsync(token);

        public Task<Dictionary<int, Team>> GetTeamDictionaryAsync(CancellationToken token = default)
            => _context.Team.ToDictionaryAsync(t => t.TeamID, token);

        public Task<Dictionary<string, Team>> GetTeamDictionaryByNameAsync(CancellationToken token = default)
            => _context.Team.ToDictionaryAsync(t => t.TeamName, token);
    }
}
