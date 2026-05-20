using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Contracts;
using SaturdayPulse.Data;
using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories
{
    /// <summary>
    /// EF Core repository for the WeeklyRankings table.
    /// Replaces the WeeklyRankings methods previously on LookupRepository.
    /// </summary>
    public class WeeklyRankingsRepository : IWeeklyRankingsRepository
    {
        private readonly NCAAContext _ctx;

        public WeeklyRankingsRepository(NCAAContext ctx) => _ctx = ctx;

        public Task<List<WeeklyRanking>> GetByYearAndWeekAsync(
            int year, int week, CancellationToken token = default)
            => _ctx.WeeklyRankings
                   .Where(wr => wr.Year == year && wr.Week == week)
                   .ToListAsync(token);

        public async Task<List<(int Year, int Week)>> GetDistinctYearWeeksAsync(
            CancellationToken token = default)
        {
            var pairs = await _ctx.WeeklyRankings
                .Select(wr => new { wr.Year, wr.Week })
                .Distinct()
                .OrderBy(x => x.Year)
                .ThenBy(x => x.Week)
                .ToListAsync(token);

            return pairs.Select(x => ((int)x.Year, (int)x.Week)).ToList();
        }

        public async Task AddAsync(WeeklyRanking ranking, CancellationToken token = default)
        {
            await _ctx.WeeklyRankings.AddAsync(ranking, token);
        }
        public Task<List<WeeklyRanking>> GetByTeamAndYearAsync(int teamId, int year, CancellationToken token = default)
            => _ctx.WeeklyRankings
                   .Where(wr => wr.TeamID == teamId && wr.Year == (short)year)
                   .OrderBy(wr => wr.Week)
                   .ToListAsync(token);
    }
}
