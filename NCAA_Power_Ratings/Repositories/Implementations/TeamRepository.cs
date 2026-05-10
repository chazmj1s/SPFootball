using Microsoft.EntityFrameworkCore;
using NCAA_Power_Ratings.Data;
using NCAA_Power_Ratings.Models;
using NCAA_Power_Ratings.Repositories.Interfaces;

namespace NCAA_Power_Ratings.Repositories.Implementations
{
    public class TeamRepository : ITeamRepository
    {
        private readonly IDbContextFactory<NCAAContext> _contextFactory;

        public TeamRepository(IDbContextFactory<NCAAContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<Team?> GetByIdAsync(
            int teamId,
            CancellationToken token = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(token);

            return await context.Team
                .FirstOrDefaultAsync(t => t.TeamID == teamId, token);
        }

        public async Task<Team?> GetByNameAsync(
            string teamName,
            CancellationToken token = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(token);

            return await context.Team
                .FirstOrDefaultAsync(t => t.TeamName == teamName, token);
        }

        public async Task<List<Team>> GetAllAsync(
            CancellationToken token = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(token);

            return await context.Team
                .OrderBy(t => t.TeamName)
                .ToListAsync(token);
        }

        public async Task<List<Team>> GetFbsTeamsAsync(
            CancellationToken token = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(token);

            return await context.Team
                .Where(t => t.Division == "FBS")
                .OrderBy(t => t.TeamName)
                .ToListAsync(token);
        }

        public async Task<Dictionary<int, Team>> GetTeamDictionaryAsync(
            CancellationToken token = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(token);

            return await context.Team
                .ToDictionaryAsync(t => t.TeamID, token);
        }

        public async Task<Dictionary<string, Team>> GetTeamDictionaryByNameAsync(
            CancellationToken token = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(token);

            return await context.Team
                .ToDictionaryAsync(t => t.TeamName, token);
        }
    }
}