using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;
using SaturdayPulse.Extensions;

namespace SaturdayPulse.Repositories.Implementations
{
    /// <summary>
    /// Repository for TeamRecords.
    ///
    /// IMPORTANT — Teams FK mismatch:
    ///   Teams.Id      = EF auto-increment PK (1, 2, 3 …)
    ///   Teams.TeamId  = CFBD team ID (e.g. 2390 for Miami)
    ///   TeamRecords.TeamID = CFBD team ID → joins to Teams.TeamId, NOT Teams.Id
    ///
    /// EF's Include(tr => tr.Teams) joins on Teams.Id (the EF PK) which is WRONG.
    /// All methods that need the Teams navigation property use a manual join on
    /// Teams.TeamId instead. Never use Include(tr => tr.Teams) in this repository.
    /// </summary>
    public class TeamRecordRepository : ITeamRecordRepository
    {
        private readonly NCAAContext _context;

        public TeamRecordRepository(NCAAContext context) => _context = context;

        // ── Simple queries (no Teams join needed) ─────────────────────────────────

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

        public Task<List<TeamRecord>> GetHistoricalAsync(
            int fromYear, int toYearExclusive, CancellationToken token = default)
            => _context.TeamRecords
                .Where(tr => tr.Year >= fromYear && tr.Year < toYearExclusive)
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

        // ── Queries requiring Teams navigation property ───────────────────────────
        // Uses manual join on Teams.TeamId (CFBD ID), NOT Teams.Id (EF PK).

        public async Task<List<TeamRecord>> GetByYearWithTeamsAsync(
            int year, CancellationToken token = default)
        {
            var records = await _context.TeamRecords
                .Where(tr => tr.Year == year)
                .ToListAsync(token);

            await PopulateTeamsAsync(records, token);
            return records;
        }

        public async Task<List<TeamRecord>> GetFbsByYearAsync(
            int year, CancellationToken token = default)
        {
            var records = await _context.TeamRecords
                .Where(tr => tr.Year == year)
                .ToListAsync(token);

            await PopulateTeamsAsync(records, token);

            return records
                .Where(tr => string.Equals(tr.Teams?.Division, "fbs",
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public async Task<List<TeamRecord>> GetSinceYearWithTeamsAsync(
            int fromYear, CancellationToken token = default)
        {
            var records = await _context.TeamRecords
                .Where(tr => tr.Year >= fromYear)
                .ToListAsync(token);

            await PopulateTeamsAsync(records, token);
            return records;
        }

        public async Task<List<TeamRecord>> QueryAsync(
            int? wins = null, int? losses = null,
            int? minWins = null, int? maxWins = null,
            int? startYear = null, int? endYear = null,
            decimal? minPowerRating = null, decimal? maxPowerRating = null,
            int limit = 50, CancellationToken token = default)
        {
            var query = _context.TeamRecords
                .Where(tr => tr.PowerRating != null)
                .AsQueryable();

            if (wins.HasValue)           query = query.Where(tr => tr.Wins        == wins.Value);
            if (losses.HasValue)         query = query.Where(tr => tr.Losses      == losses.Value);
            if (minWins.HasValue)        query = query.Where(tr => tr.Wins        >= minWins.Value);
            if (maxWins.HasValue)        query = query.Where(tr => tr.Wins        <= maxWins.Value);
            if (startYear.HasValue)      query = query.Where(tr => tr.Year        >= startYear.Value);
            if (endYear.HasValue)        query = query.Where(tr => tr.Year        <= endYear.Value);
            if (minPowerRating.HasValue) query = query.Where(tr => tr.PowerRating >= minPowerRating.Value);
            if (maxPowerRating.HasValue) query = query.Where(tr => tr.PowerRating <= maxPowerRating.Value);

            var records = await query
                .OrderByDescending(tr => tr.Year)
                .ThenByDescending(tr => tr.PowerRating)
                .Take(limit)
                .ToListAsync(token);

            await PopulateTeamsAsync(records, token);
            return records;
        }

        // ── Upsert methods ────────────────────────────────────────────────────────

        public async Task UpsertFromWeeklyRankingsAsync(int? targetYear = null, CancellationToken token = default)
        {
            var maxWeek = await _context.WeeklyRankings
                .Where(wr => wr.Year == targetYear)
                .MaxAsync(x => x.Week, token);

            var rankings = await _context.WeeklyRankings
                .Where(wr => wr.Year == targetYear && wr.Week == maxWeek)
                .ToListAsync(token);

            var existing = await _context.TeamRecords
                .Where(tr => tr.Year == targetYear)
                .ToDictionaryAsync(tr => tr.TeamID, token);

            rankings.UpdateTeamRecords(existing);

            var newRecords = rankings
                .Where(r => !existing.ContainsKey(r.TeamID))
                .ToTeamRecords();

            await _context.TeamRecords.AddRangeAsync(newRecords, token);
            await _context.SaveChangesAsync(token);
        }

        /// <summary>
        /// Upserts TeamRecords from the Games table.
        /// For unplayed games, projected scores are substituted from Projections table.
        /// Synthetic scores are never written to the Games table.
        /// </summary>
        public async Task UpsertFromGamesAsync(int? targetYear = null, CancellationToken token = default)
        {
            var query = _context.Games.Where(g => g.Year > 0);
            if (targetYear.HasValue)
                query = query.Where(g => g.Year == targetYear.Value);

            var games = await query.ToListAsync(token);

            var unplayedGameIds = games
                .Where(g => (g.HomePoints ?? 0) == 0 && (g.AwayPoints ?? 0) == 0)
                .Select(g => g.GameId)
                .ToList();

            if (unplayedGameIds.Any())
            {
                var projections = await _context.Projections
                    .Where(p => unplayedGameIds.Contains(p.GameId))
                    .GroupBy(p => p.GameId)
                    .Select(grp => grp.OrderByDescending(p => p.Week).First())
                    .ToListAsync(token);

                var projByGameId = projections.ToDictionary(p => p.GameId);

                foreach (var g in games.Where(g =>
                    (g.HomePoints ?? 0) == 0 && (g.AwayPoints ?? 0) == 0))
                {
                    if (!projByGameId.TryGetValue(g.GameId, out var proj)) continue;

                    g.HomePoints = (int)Math.Round(
                        (double)(proj.PredictedTotal + proj.PredictedSpread) / 2.0);
                    g.AwayPoints = (int)Math.Round(
                        (double)(proj.PredictedTotal - proj.PredictedSpread) / 2.0);

                    if (g.HomePoints == 0 && g.AwayPoints == 0) g.HomePoints = 14;
                }

                games = games
                    .Where(g => (g.HomePoints ?? 0) > 0 || (g.AwayPoints ?? 0) > 0)
                    .ToList();
            }

            var homeTeams = games
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

            var awayTeams = games
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

            var grouped = homeTeams.Concat(awayTeams)
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
                .ToList();

            if (grouped.Count == 0) return;

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
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Populates the Teams navigation property on a list of TeamRecords using
        /// a manual join on Teams.TeamId (CFBD ID) rather than Teams.Id (EF PK).
        /// This is necessary because TeamRecords.TeamID = CFBD ID, not the EF auto-increment.
        /// </summary>
        private async Task PopulateTeamsAsync(
            List<TeamRecord> records, CancellationToken token)
        {
            if (!records.Any()) return;

            var teamIds = records.Select(r => r.TeamID).Distinct().ToList();
            var teams   = await _context.Teams
                .AsNoTracking()
                .Where(t => teamIds.Contains(t.TeamId))
                .ToDictionaryAsync(t => t.TeamId, token);

            foreach (var record in records)
            {
                var team = teams.GetValueOrDefault(record.TeamID);
                if (team != null)
                {
                    // Detach from change tracker before assigning to avoid conflicts
                    _context.Entry(record).State = EntityState.Detached;
                    record.Teams = team;
                }
            }
        }
    }
}
