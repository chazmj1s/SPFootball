using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
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
                .Include(tr => tr.Teams)
                .Where(tr => tr.Year == year)
                .ToListAsync(token);

        public Task<List<TeamRecord>> GetFbsByYearAsync(int year, CancellationToken token = default)
            => _context.TeamRecords
                .Include(tr => tr.Teams)
                .Where(tr => tr.Year == year &&
                             tr.Teams != null &&
                             tr.Teams.Division == "FBS")
                .ToListAsync(token);

        public Task<List<TeamRecord>> GetHistoricalAsync(
            int fromYear, int toYearExclusive, CancellationToken token = default)
            => _context.TeamRecords
                .Where(tr => tr.Year >= fromYear && tr.Year < toYearExclusive)
                .ToListAsync(token);

        public Task<List<TeamRecord>> GetSinceYearWithTeamsAsync(
            int fromYear, CancellationToken token = default)
            => _context.TeamRecords
                .Include(tr => tr.Teams)
                .Where(tr => tr.Year >= fromYear)
                .ToListAsync(token);

        public Task<List<TeamRecord>> GetByTeamAllYearsAsync(
            int teamId, CancellationToken token = default)
            => _context.TeamRecords
                .Where(tr => tr.TeamID == teamId)
                .OrderBy(tr => tr.Year)
                .ToListAsync(token);

        public async Task<List<TeamRecord>> GetRankedByYearAsync(int year, CancellationToken token = default)
        {
            var records = await _context.TeamRecords
                .Where(tr => tr.Year == year && tr.Ranking.HasValue)
                .ToListAsync(token);

            return records.OrderByDescending(tr => tr.Ranking).ToList();
        }

        /// <summary>
        /// Aggregates wins/losses/points from the Games table (CFBD V2, home/away model)
        /// and upserts into TeamRecords.
        /// </summary>
        public async Task UpsertFromGamesAsync(int? targetYear = null, CancellationToken token = default)
        {
            var query = _context.Games
                .Where(g => g.Year > 0 &&
                            ((g.HomePoints ?? 0) > 0 || (g.AwayPoints ?? 0) > 0));

            if (targetYear.HasValue)
                query = query.Where(g => g.Year == targetYear.Value);

            var homeTeams = query
                .Where(g => g.HomeId != null)
                .Select(g => new
                {
                    Year          = g.Year,
                    TeamId        = g.HomeId!.Value,
                    Wins          = (g.HomePoints ?? 0) > (g.AwayPoints ?? 0) ? 1 : 0,
                    Losses        = (g.HomePoints ?? 0) > (g.AwayPoints ?? 0) ? 0 : 1,
                    PointsFor     = g.HomePoints ?? 0,
                    PointsAgainst = g.AwayPoints ?? 0
                });

            var awayTeams = query
                .Where(g => g.AwayId != null)
                .Select(g => new
                {
                    Year          = g.Year,
                    TeamId        = g.AwayId!.Value,
                    Wins          = (g.AwayPoints ?? 0) > (g.HomePoints ?? 0) ? 1 : 0,
                    Losses        = (g.AwayPoints ?? 0) > (g.HomePoints ?? 0) ? 0 : 1,
                    PointsFor     = g.AwayPoints ?? 0,
                    PointsAgainst = g.HomePoints ?? 0
                });

            var grouped = await homeTeams.Concat(awayTeams)
                .GroupBy(x => new { x.Year, x.TeamId })
                .Select(g => new
                {
                    Year          = g.Key.Year,
                    TeamId        = g.Key.TeamId,
                    Wins          = g.Sum(x => x.Wins),
                    Losses        = g.Sum(x => x.Losses),
                    PointsFor     = g.Sum(x => x.PointsFor),
                    PointsAgainst = g.Sum(x => x.PointsAgainst)
                })
                .ToListAsync(token);

            if (grouped.Count == 0) return;

            // Filter out FCS placeholder IDs (1000xxx) and any team not in the Teams table.
            var validTeamIds = (await _context.Teams
                .Select(t => t.TeamId)
                .ToListAsync(token))
                .ToHashSet();

            var aggregated = grouped
                .Where(g => validTeamIds.Contains(g.TeamId))
                .Select(g => new TeamRecord
                {
                    TeamID        = g.TeamId,
                    Year          = (short)g.Year,
                    Wins          = (byte)g.Wins,
                    Losses        = (byte)g.Losses,
                    PointsFor     = g.PointsFor,
                    PointsAgainst = g.PointsAgainst
                }).ToList();

            var years           = aggregated.Select(a => a.Year).Distinct().ToList();
            var existingRecords = await _context.TeamRecords
                .Where(tr => years.Contains(tr.Year))
                .ToListAsync(token);

            foreach (var rec in aggregated)
            {
                var exist = existingRecords
                    .FirstOrDefault(e => e.TeamID == rec.TeamID && e.Year == rec.Year);
                if (exist != null)
                {
                    exist.Wins          = rec.Wins;
                    exist.Losses        = rec.Losses;
                    exist.PointsFor     = rec.PointsFor;
                    exist.PointsAgainst = rec.PointsAgainst;
                }
                else
                {
                    _context.TeamRecords.Add(rec);
                }
            }

            // Debug — log what EF is tracking before save
            var tracked = _context.ChangeTracker.Entries()
                .Where(e => e.State != Microsoft.EntityFrameworkCore.EntityState.Unchanged)
                .Select(e => new { Type = e.Entity.GetType().Name, e.State })
                .ToList();

            foreach (var t in tracked)
                Console.WriteLine($"Tracked: {t.Type} - {t.State}");

            // SaveChanges called through IUnitOfWork.SaveChangesAsync — not here.
            // SaveChanges called through IUnitOfWork.SaveChangesAsync — not here.
        }

        public async Task<List<TeamRecord>> QueryAsync(
            int? wins = null, int? losses = null,
            int? minWins = null, int? maxWins = null,
            int? startYear = null, int? endYear = null,
            decimal? minPowerRating = null, decimal? maxPowerRating = null,
            int limit = 50, CancellationToken token = default)
        {
            var query = _context.TeamRecords
                .Include(tr => tr.Teams)
                .Where(tr => tr.PowerRating != null)
                .AsQueryable();

            if (wins.HasValue)           query = query.Where(tr => tr.Wins   == wins.Value);
            if (losses.HasValue)         query = query.Where(tr => tr.Losses == losses.Value);
            if (minWins.HasValue)        query = query.Where(tr => tr.Wins   >= minWins.Value);
            if (maxWins.HasValue)        query = query.Where(tr => tr.Wins   <= maxWins.Value);
            if (startYear.HasValue)      query = query.Where(tr => tr.Year   >= startYear.Value);
            if (endYear.HasValue)        query = query.Where(tr => tr.Year   <= endYear.Value);
            if (minPowerRating.HasValue) query = query.Where(tr => tr.PowerRating >= minPowerRating.Value);
            if (maxPowerRating.HasValue) query = query.Where(tr => tr.PowerRating <= maxPowerRating.Value);

            return await query
                .OrderByDescending(tr => tr.Year)
                .ThenByDescending(tr => tr.PowerRating)
                .Take(limit)
                .ToListAsync(token);
        }
    }
}
