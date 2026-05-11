using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NCAA_Power_Ratings.Configuration;
using NCAA_Power_Ratings.Contracts;
using NCAA_Power_Ratings.Data;
using System.Text.Json;

namespace NCAA_Power_Ratings.Services
{
    /// <summary>
    /// Calculates SOS, PowerRating, and Rankings for all FBS teams in a given year.
    ///
    /// Pipeline order (must be sequential):
    ///   RollingAverageService.ComputeAndPersistAsync
    ///     → SetSOS → CalculatePowerRatings → CalculateRankings
    ///
    /// Pass 1: IDbContextFactory replaced with IUnitOfWork.
    /// Pass 2 (next): direct EF queries moved into repositories.
    /// </summary>
    public class TeamMetricsService
    {
        private readonly IUnitOfWork          _uow;
        private readonly MetricsConfiguration _config;

        // Direct context access retained for Pass 2 migration —
        // the complex LINQ joins in SetSOS and CalculatePowerRatings
        // will move to repositories in the next pass.
        private readonly NCAAContext _context;

        public TeamMetricsService(
            IUnitOfWork uow,
            IOptions<MetricsConfiguration> config)
        {
            _uow     = uow;
            _config  = config.Value;
            // Context is resolved from the UoW's shared instance via a
            // temporary cast until Pass 2 moves queries to repositories.
            _context = ((Infrastructure.UnitOfWork)uow).Context;
        }

        // ── CalculateTrend ────────────────────────────────────────────────────────

        public async Task<string> CalculateTrend(CancellationToken token = default)
        {
            try
            {
                var standardSeasonGames = 12;
                const double extraWinBump = 0.25;

                var currentYear = DateTime.Now.Year;
                var startYear   = currentYear - 10;

                var teamRecords = await _context.TeamRecords
                    .Where(tr => tr.Year >= startYear)
                    .Include(tr => tr.Team)
                    .ToListAsync(token);

                var results = teamRecords
                    .GroupBy(tr => tr.TeamID)
                    .Select(g =>
                    {
                        var records = g.OrderBy(r => r.Year).TakeLast(10).ToList();

                        var normalizedPercentages = records.Select(r =>
                        {
                            standardSeasonGames = r.RegularSeasonGames;
                            int totalGames = r.Wins + r.Losses;
                            if (totalGames <= 0) return 0.0;

                            if (totalGames <= standardSeasonGames)
                                return Math.Round((double)r.Wins / totalGames, 4);

                            int baseWins  = Math.Min(r.Wins, standardSeasonGames);
                            int extraWins = Math.Max(0, r.Wins - standardSeasonGames);
                            return Math.Round((baseWins + extraWins * extraWinBump) / standardSeasonGames, 4);
                        }).ToList();

                        double weightedAverage = 0.0;
                        if (normalizedPercentages.Count > 0)
                        {
                            int n = normalizedPercentages.Count;
                            long weightSum = (long)n * (n + 1) / 2;
                            weightedAverage = normalizedPercentages
                                .Select((pct, index) => pct * (index + 1))
                                .Sum() / weightSum;
                        }

                        double projectedWinsDecimal = weightedAverage * standardSeasonGames;
                        int projectedWins = projectedWinsDecimal - Math.Floor(projectedWinsDecimal) >= 0.75
                            ? (int)Math.Ceiling(projectedWinsDecimal)
                            : (int)Math.Floor(projectedWinsDecimal);

                        return new
                        {
                            TeamID                   = g.Key,
                            TeamName                 = g.First().Team?.TeamName ?? "Unknown",
                            Years                    = records.Select(r => r.Year).ToList(),
                            NormalizedWinPercentages = normalizedPercentages,
                            WeightedAverage          = Math.Round(weightedAverage, 4),
                            ProjectedWins            = projectedWins,
                            RecordCount              = records.Count
                        };
                    })
                    .OrderByDescending(r => r.WeightedAverage)
                    .ToList();

                return JsonSerializer.Serialize(results, new JsonSerializerOptions
                {
                    WriteIndented        = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calculating team trends: {ex.Message}");
                throw;
            }
        }

        // ── SetSOS ────────────────────────────────────────────────────────────────

        public async Task SetSOS(int? year = null, int? week = null, CancellationToken token = default)
        {
            try
            {
                var targetYear = year ?? DateTime.Now.Year;
                var targetWeek = week ?? 0;

                Dictionary<int, int> winsLookup;
                Dictionary<int, int> lossesLookup;

                if (targetWeek == 0)
                {
                    var seedRecords = await _context.TeamRecords
                        .Where(tr => tr.Year == targetYear && tr.SeedRating != null)
                        .ToListAsync(token);

                    winsLookup = seedRecords.ToDictionary(
                        tr => tr.TeamID,
                        tr => (int)Math.Round((double)tr.SeedRating!.Value * tr.RegularSeasonGames));

                    lossesLookup = seedRecords.ToDictionary(
                        tr => tr.TeamID,
                        tr => tr.RegularSeasonGames -
                              (int)Math.Round((double)tr.SeedRating!.Value * tr.RegularSeasonGames));
                }
                else
                {
                    if (targetWeek < _config.SosWeekThreshold) return;

                    var teamRecords = await _context.TeamRecords
                        .Where(tr => tr.Year == targetYear)
                        .ToDictionaryAsync(tr => tr.TeamID, token);

                    winsLookup   = teamRecords.ToDictionary(kvp => kvp.Key, kvp => (int)kvp.Value.Wins);
                    lossesLookup = teamRecords.ToDictionary(kvp => kvp.Key, kvp => (int)kvp.Value.Losses);
                }

                var gamesFromWinner = from g in _context.Game
                    where g.Year == targetYear
                    join t   in _context.Team on g.WinnerId equals t.TeamID
                    join opp in _context.Team on g.LoserId  equals opp.TeamID
                    select new
                    {
                        g.Year,
                        TeamId = g.WinnerId, TeamName = g.WinnerName, TeamDivision = t.Division,
                        OpponentId = g.LoserId, OpponentName = g.LoserName, OpponentDivision = opp.Division,
                        TeamPoints = g.WPoints, OpponentPoints = g.LPoints,
                        g.Location, IsHomeTeam = g.Location == 'W'
                    };

                var gamesFromLoser = from g in _context.Game
                    where g.Year == targetYear
                    join t   in _context.Team on g.LoserId  equals t.TeamID
                    join opp in _context.Team on g.WinnerId equals opp.TeamID
                    select new
                    {
                        g.Year,
                        TeamId = g.LoserId, TeamName = g.LoserName, TeamDivision = t.Division,
                        OpponentId = g.WinnerId, OpponentName = g.WinnerName, OpponentDivision = opp.Division,
                        TeamPoints = g.LPoints, OpponentPoints = g.WPoints,
                        g.Location, IsHomeTeam = g.Location == 'L'
                    };

                var gameParticipants = await gamesFromWinner.Union(gamesFromLoser).ToListAsync(token);

                var withRecords = gameParticipants.Select(gp => new
                {
                    gp.Year, gp.TeamId, gp.TeamName, gp.TeamDivision,
                    TeamWins     = winsLookup.GetValueOrDefault(gp.TeamId,     0),
                    TeamLosses   = lossesLookup.GetValueOrDefault(gp.TeamId,   0),
                    gp.OpponentId, gp.OpponentName, gp.OpponentDivision,
                    gp.TeamPoints, gp.OpponentPoints,
                    OpponentWins   = winsLookup.GetValueOrDefault(gp.OpponentId,   0),
                    OpponentLosses = lossesLookup.GetValueOrDefault(gp.OpponentId, 0),
                    gp.Location, gp.IsHomeTeam
                }).ToList();

                var hfa              = _config.HomeFieldAdvantage;
                var avgScoreDeltas   = await _context.AvgScoreDeltas.ToListAsync(token);
                var matchupHistories = await _context.MatchupHistories.ToListAsync(token);

                var withDeltas = withRecords.Select(r =>
                {
                    var delta          = r.TeamPoints - r.OpponentPoints;
                    var teamGames      = r.TeamWins + r.TeamLosses;
                    var oppGames       = r.OpponentWins + r.OpponentLosses;
                    var teamWinPct     = RatingCalculator.BucketWinPct(r.TeamWins, teamGames);
                    var oppWinPct      = RatingCalculator.BucketWinPct(r.OpponentWins, oppGames);
                    var maxWinPct      = Math.Max(teamWinPct, oppWinPct);
                    var minWinPct      = Math.Min(teamWinPct, oppWinPct);
                    var asd            = avgScoreDeltas.FirstOrDefault(a => a.Team1WinPct == maxWinPct && a.Team2WinPct == minWinPct);
                    var rivalryTier    = matchupHistories.FirstOrDefault(m =>
                        m.Team1Id == Math.Min(r.TeamId, r.OpponentId) &&
                        m.Team2Id == Math.Max(r.TeamId, r.OpponentId))?.RivalryTier;

                    double zValue = 0.0;
                    if (asd != null && asd.StDevP != 0)
                    {
                        var expected       = RatingCalculator.ExpectedFromPerspective((double)asd.AverageScoreDelta, teamWinPct, oppWinPct);
                        expected           = RatingCalculator.ApplyHomeField(expected, r.IsHomeTeam, r.Location == 'N', hfa);
                        var effectiveStDev = (double)asd.StDevP * RatingCalculator.RivalryVarianceMultiplier(rivalryTier);
                        zValue             = (delta - expected) / effectiveStDev;
                    }

                    return new
                    {
                        r.Year, r.TeamId, r.TeamName, r.TeamDivision, r.TeamWins,
                        r.OpponentId, r.OpponentName, r.OpponentDivision,
                        r.TeamPoints, r.OpponentPoints, Delta = delta, ZValue = zValue
                    };
                }).ToList();

                var withWeights = withDeltas.Select(d => new
                {
                    d.Year, d.TeamId, d.TeamName, d.OpponentId, d.OpponentName, d.OpponentDivision,
                    Weight = d.ZValue switch { >= 1.0 => 1.25, > -1.0 => 1.00, > -2.0 => 0.75, _ => 0.50 },
                    DivisionWeight = RatingCalculator.DivisionWeight(d.OpponentDivision)
                }).ToList();

                var baseSOS = withWeights
                    .GroupBy(w => new { w.Year, w.TeamId })
                    .Select(g => new
                    {
                        g.Key.Year, g.Key.TeamId,
                        BaseSOS = Math.Round(g.Sum(x => x.Weight * x.DivisionWeight) / g.Sum(x => x.DivisionWeight), 3),
                        GamesPlayed = g.Count()
                    }).ToList();

                var opponentSOS = withWeights
                    .Join(baseSOS,
                        w => new { w.Year, TeamId = w.OpponentId },
                        b => new { Year  = b.Year, b.TeamId },
                        (w, b) => new { w.Year, w.TeamId, OppBaseSOS = b.BaseSOS, w.Weight })
                    .ToList();

                var secondOrderSOS = opponentSOS
                    .GroupBy(o => new { o.Year, o.TeamId })
                    .Select(g => new
                    {
                        g.Key.Year, g.Key.TeamId,
                        SubSOS = Math.Round(g.Sum(x => x.OppBaseSOS * x.Weight) / g.Sum(x => x.Weight), 3)
                    }).ToList();

                var combined = baseSOS
                    .GroupJoin(secondOrderSOS,
                        b => new { b.Year, b.TeamId },
                        s => new { s.Year, s.TeamId },
                        (b, s) => new
                        {
                            b.Year, b.TeamId, b.BaseSOS,
                            SubSOS = s.FirstOrDefault()?.SubSOS ?? b.BaseSOS
                        })
                    .Select(c => new
                    {
                        c.Year, c.TeamId, c.BaseSOS, c.SubSOS,
                        CombinedSOS = Math.Round((2 * c.BaseSOS + 3 * c.SubSOS) / 5, 4)
                    }).ToList();

                var teamRecordsToUpdate = await _context.TeamRecords
                    .Where(tr => tr.Year == targetYear)
                    .Include(tr => tr.Team)
                    .ToListAsync(token);

                foreach (var record in teamRecordsToUpdate)
                {
                    var sosData = combined.FirstOrDefault(c => c.TeamId == record.TeamID && c.Year == record.Year);
                    if (record.Team.Division == "FCS")
                    {
                        record.BaseSOS = record.SubSOS = record.CombinedSOS = null;
                    }
                    else if (sosData != null)
                    {
                        record.BaseSOS     = (decimal)sosData.BaseSOS;
                        record.SubSOS      = (decimal)sosData.SubSOS;
                        record.CombinedSOS = (decimal)sosData.CombinedSOS;
                    }
                }

                await _uow.SaveChangesAsync(token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calculating SOS: {ex.Message}");
                throw;
            }
        }

        // ── CalculatePowerRatings ─────────────────────────────────────────────────

        public async Task CalculatePowerRatings(int? year = null, CancellationToken token = default)
        {
            var targetYear = year ?? DateTime.Now.Year;

            var gamesFromWinner = from g in _context.Game
                where g.Year == targetYear
                join t   in _context.Team on g.WinnerId equals t.TeamID
                join opp in _context.Team on g.LoserId  equals opp.TeamID
                select new
                {
                    g.Year,
                    TeamId = g.WinnerId, TeamDivision = t.Division,
                    OpponentId = g.LoserId, OpponentDivision = opp.Division,
                    TeamPoints = g.WPoints, OpponentPoints = g.LPoints,
                    g.Location, IsHomeTeam = g.Location == 'W'
                };

            var gamesFromLoser = from g in _context.Game
                where g.Year == targetYear
                join t   in _context.Team on g.LoserId  equals t.TeamID
                join opp in _context.Team on g.WinnerId equals opp.TeamID
                select new
                {
                    g.Year,
                    TeamId = g.LoserId, TeamDivision = t.Division,
                    OpponentId = g.WinnerId, OpponentDivision = opp.Division,
                    TeamPoints = g.LPoints, OpponentPoints = g.WPoints,
                    g.Location, IsHomeTeam = g.Location == 'L'
                };

            var gameParticipants = await gamesFromWinner.Union(gamesFromLoser).ToListAsync(token);

            var teamRecords      = await _context.TeamRecords
                .Where(tr => tr.Year == targetYear)
                .ToDictionaryAsync(tr => tr.TeamID, token);

            var winsLookup   = teamRecords.ToDictionary(kvp => kvp.Key, kvp => (int)kvp.Value.Wins);
            var lossesLookup = teamRecords.ToDictionary(kvp => kvp.Key, kvp => (int)kvp.Value.Losses);

            var avgScoreDeltas   = await _context.AvgScoreDeltas.ToListAsync(token);
            var matchupHistories = await _context.MatchupHistories.ToListAsync(token);
            var hfa              = _config.HomeFieldAdvantage;

            var zScores = gameParticipants
                .Select(gp =>
                {
                    var teamWins   = winsLookup.GetValueOrDefault(gp.TeamId,     0);
                    var teamLosses = lossesLookup.GetValueOrDefault(gp.TeamId,   0);
                    var oppWins    = winsLookup.GetValueOrDefault(gp.OpponentId, 0);
                    var oppLosses  = lossesLookup.GetValueOrDefault(gp.OpponentId, 0);
                    var teamWinPct = RatingCalculator.BucketWinPct(teamWins, teamWins + teamLosses);
                    var oppWinPct  = RatingCalculator.BucketWinPct(oppWins,  oppWins  + oppLosses);
                    var maxWinPct  = Math.Max(teamWinPct, oppWinPct);
                    var minWinPct  = Math.Min(teamWinPct, oppWinPct);
                    var delta      = gp.TeamPoints - gp.OpponentPoints;
                    var asd        = avgScoreDeltas.FirstOrDefault(a => a.Team1WinPct == maxWinPct && a.Team2WinPct == minWinPct);
                    var rivalryTier = matchupHistories.FirstOrDefault(m =>
                        m.Team1Id == Math.Min(gp.TeamId, gp.OpponentId) &&
                        m.Team2Id == Math.Max(gp.TeamId, gp.OpponentId))?.RivalryTier;

                    double zScore = 0.0;
                    if (asd != null && asd.StDevP != 0)
                    {
                        var expected       = RatingCalculator.ExpectedFromPerspective((double)asd.AverageScoreDelta, teamWinPct, oppWinPct);
                        expected           = RatingCalculator.ApplyHomeField(expected, gp.IsHomeTeam, gp.Location == 'N', hfa);
                        var effectiveStDev = (double)asd.StDevP * RatingCalculator.RivalryVarianceMultiplier(rivalryTier);
                        zScore             = RatingCalculator.DampenZScore((delta - expected) / effectiveStDev);
                    }

                    return new
                    {
                        gp.TeamId, gp.TeamDivision,
                        ZScore        = zScore,
                        DivisionWeight = RatingCalculator.DivisionWeight(gp.OpponentDivision)
                    };
                })
                .GroupBy(x => x.TeamId)
                .Select(g => new
                {
                    TeamId       = g.Key,
                    TeamDivision = g.First().TeamDivision,
                    AvgZScore    = g.Sum(x => x.ZScore * x.DivisionWeight) / g.Sum(x => x.DivisionWeight)
                }).ToList();

            var teamRecordsForUpdate = await _context.TeamRecords
                .Where(tr => tr.Year == targetYear)
                .Include(tr => tr.Team)
                .ToListAsync(token);

            foreach (var record in teamRecordsForUpdate)
            {
                if (record.Team.Division == "FCS") { record.PowerRating = null; continue; }

                var zData = zScores.FirstOrDefault(z => z.TeamId == record.TeamID);
                if (zData != null)
                {
                    var sos = (double)(record.CombinedSOS ?? 1.0m);
                    record.PowerRating = (decimal)Math.Round(zData.AvgZScore * sos, 4);
                }
            }

            await _uow.SaveChangesAsync(token);
        }

        // ── CalculateRankings ─────────────────────────────────────────────────────

        public async Task CalculateRankings(int targetYear, CancellationToken token = default)
        {
            var teamRecords = await _context.TeamRecords
                .Where(tr => tr.Year == targetYear)
                .Include(tr => tr.Team)
                .ToListAsync(token);

            foreach (var record in teamRecords)
            {
                if (record.Team.Division == "FCS") { record.Ranking = null; continue; }

                var totalGames = record.Wins + record.Losses;
                if (totalGames > 0 && record.CombinedSOS.HasValue && record.PowerRating.HasValue)
                {
                    var winPct = (decimal)record.Wins / totalGames;
                    record.Ranking = Math.Round(winPct * record.CombinedSOS.Value * (1 + record.PowerRating.Value), 4);
                }
                else
                {
                    record.Ranking = null;
                }
            }

            await _uow.SaveChangesAsync(token);
        }
    }
}
