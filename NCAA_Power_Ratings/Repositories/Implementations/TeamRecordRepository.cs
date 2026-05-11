using Microsoft.EntityFrameworkCore;
using NCAA_Power_Ratings.Data;
using NCAA_Power_Ratings.Models;
using NCAA_Power_Ratings.Repositories.Interfaces;

namespace NCAA_Power_Ratings.Repositories.Implementations
{
    public class TeamRecordRepository : ITeamRecordRepository
    {
        private readonly NCAAContext _context;

        public TeamRecordRepository(NCAAContext context) => _context = context;

        public Task<List<TeamRecord>> GetByYearAsync(int year, CancellationToken token = default)
            => _context.TeamRecords
                .Where(tr => tr.Year == year)
                .ToListAsync(token);

        public Task<TeamRecord?> GetByTeamAndYearAsync(int teamId, int year, CancellationToken token = default)
            => _context.TeamRecords
                .FirstOrDefaultAsync(tr => tr.TeamID == teamId && tr.Year == year, token);

        public Task<Dictionary<int, TeamRecord>> GetByTeamsAndYearAsync(
            IEnumerable<int> teamIds, int year, CancellationToken token = default)
            => _context.TeamRecords
                .Where(tr => tr.Year == year && teamIds.Contains(tr.TeamID))
                .ToDictionaryAsync(tr => tr.TeamID, token);

        public Task<List<TeamRecord>> GetByYearWithTeamsAsync(int year, CancellationToken token = default)
            => _context.TeamRecords
                .Include(tr => tr.Team)
                .Where(tr => tr.Year == year)
                .ToListAsync(token);

        public Task<List<TeamRecord>> GetFbsByYearAsync(int year, CancellationToken token = default)
            => _context.TeamRecords
                .Include(tr => tr.Team)
                .Where(tr => tr.Year == year &&
                             tr.Team != null &&
                             tr.Team.Division == "FBS")
                .ToListAsync(token);

        public async Task<List<TeamRecord>> QueryAsync(
            int? wins = null, int? losses = null,
            int? minWins = null, int? maxWins = null,
            int? startYear = null, int? endYear = null,
            decimal? minPowerRating = null, decimal? maxPowerRating = null,
            int limit = 50, CancellationToken token = default)
        {
            var query = _context.TeamRecords
                .Include(tr => tr.Team)
                .Where(tr => tr.PowerRating != null)
                .AsQueryable();

            if (wins.HasValue)           query = query.Where(tr => tr.Wins == wins.Value);
            if (losses.HasValue)         query = query.Where(tr => tr.Losses == losses.Value);
            if (minWins.HasValue)        query = query.Where(tr => tr.Wins >= minWins.Value);
            if (maxWins.HasValue)        query = query.Where(tr => tr.Wins <= maxWins.Value);
            if (startYear.HasValue)      query = query.Where(tr => tr.Year >= startYear.Value);
            if (endYear.HasValue)        query = query.Where(tr => tr.Year <= endYear.Value);
            if (minPowerRating.HasValue) query = query.Where(tr => tr.PowerRating >= minPowerRating.Value);
            if (maxPowerRating.HasValue) query = query.Where(tr => tr.PowerRating <= maxPowerRating.Value);

            return await query
                .OrderByDescending(tr => tr.Year)
                .ThenByDescending(tr => tr.PowerRating)
                .Take(limit)
                .ToListAsync(token);
        }

        public Task<List<TeamRecord>> GetHistoricalAsync(
    int fromYear, int toYearExclusive, CancellationToken token = default)
    => _context.TeamRecords
        .Where(tr => tr.Year >= fromYear && tr.Year < toYearExclusive)
        .ToListAsync(token);
    }
}
