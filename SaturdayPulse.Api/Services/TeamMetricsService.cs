using Microsoft.Extensions.Options;
using SaturdayPulse.Configuration;
using SaturdayPulse.Contracts;
using System.Text.Json;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Calculates SOS, PowerRating, and Rankings for all FBS teams in a given year.
    ///
    /// Pipeline order (must be sequential):
    ///   RollingAverageService.ComputeAndPersistAsync
    ///     → SetSOS → CalculatePowerRatings → CalculateRankings
    ///
    /// Pass 2 complete: all EF queries moved to repositories.
    /// No direct _context references remain.
    /// </summary>
    public class TeamMetricsService
    {
        private readonly IUnitOfWork          _uow;
        private readonly MetricsConfiguration _config;

        public TeamMetricsService(
            IUnitOfWork uow,
            IOptions<MetricsConfiguration> config)
        {
            _uow    = uow;
            _config = config.Value;
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

                var teamRecords = await _uow.TeamRecords.GetSinceYearWithTeamsAsync(startYear, token);

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
                            TeamName                 = g.First().Teams?.TeamName ?? "Unknown",
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

                // ── Step 1: Win/loss source ───────────────────────────────────────
                Dictionary<int, int> winsLookup;
                Dictionary<int, int> lossesLookup;

                if (targetWeek == 0)
                {
                    var seedRecords = await _uow.TeamRecords.GetByYearAsync(targetYear, token);
                    seedRecords = seedRecords.Where(tr => tr.SeedRating != null).ToList();

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

                    var teamRecords = await _uow.TeamRecords.GetByYearAsync(targetYear, token);
                    winsLookup   = teamRecords.ToDictionary(tr => tr.TeamID, tr => (int)tr.Wins);
                    lossesLookup = teamRecords.ToDictionary(tr => tr.TeamID, tr => (int)tr.Losses);
                }

                // ── Step 2: Game participants ─────────────────────────────────────
                var gameParticipants = await _uow.Games.GetGameParticipantsAsync(targetYear, token);

                // ── Step 3: Annotate with wins/losses ─────────────────────────────
                var withRecords = gameParticipants.Select(gp => new
                {
                    gp.TeamId, gp.TeamDivision,
                    TeamWins     = winsLookup.GetValueOrDefault(gp.TeamId,     0),
                    TeamLosses   = lossesLookup.GetValueOrDefault(gp.TeamId,   0),
                    gp.OpponentId, gp.OpponentDivision,
                    gp.TeamPoints, gp.OpponentPoints,
                    OpponentWins   = winsLookup.GetValueOrDefault(gp.OpponentId,   0),
                    OpponentLosses = lossesLookup.GetValueOrDefault(gp.OpponentId, 0),
                    gp.Location, gp.IsHomeTeam
                }).ToList();

                // ── Step 4: Z-scores ──────────────────────────────────────────────
                var avgScoreDeltas   = await _uow.Lookups.GetAvgScoreDeltasAsync(token);
                var matchupHistories = await _uow.Lookups.GetMatchupHistoriesAsync(token);
                var hfa              = _config.HomeFieldAdvantage;

                var withDeltas = withRecords.Select(r =>
                {
                    var delta      = r.TeamPoints - r.OpponentPoints;
                    var teamWinPct = RatingCalculator.BucketWinPct(r.TeamWins, r.TeamWins + r.TeamLosses);
                    var oppWinPct  = RatingCalculator.BucketWinPct(r.OpponentWins, r.OpponentWins + r.OpponentLosses);
                    var maxWinPct  = Math.Max(teamWinPct, oppWinPct);
                    var minWinPct  = Math.Min(teamWinPct, oppWinPct);
                    var asd        = avgScoreDeltas.FirstOrDefault(a => a.Team1WinPct == maxWinPct && a.Team2WinPct == minWinPct);
                    var rivalryTier = matchupHistories.FirstOrDefault(m =>
                        m.Team1Id == Math.Min(r.TeamId, r.OpponentId) &&
                        m.Team2Id == Math.Max(r.TeamId, r.OpponentId))?.RivalryTier;

                    double zValue = 0.0;
                    if (asd != null && asd.StDevP != 0)
                    {
                        var expected       = RatingCalculator.ExpectedFromPerspective((double)asd.AverageScoreDelta, teamWinPct, oppWinPct);
                        expected           = RatingCalculator.ApplyHomeField(expected, r.IsHomeTeam, r.Location == 'N', hfa);
                        var effectiveStDev = (double)asd.WeightedStdDev * RatingCalculator.RivalryVarianceMultiplier(rivalryTier);
                        zValue             = RatingCalculator.DampenZScore((delta - expected) / effectiveStDev);
                    }

                    return new
                    {
                        r.TeamId, r.TeamDivision, r.OpponentId, r.OpponentDivision,
                        Delta = delta, ZValue = zValue
                    };
                }).ToList();

                // ── Steps 5-9: BaseSOS → SubSOS → CombinedSOS ────────────────────
                var withWeights = withDeltas.Select(d => new
                {
                    d.TeamId, d.OpponentId, d.OpponentDivision,
                    Weight         = d.ZValue switch { >= 1.0 => 1.25, > -1.0 => 1.00, > -2.0 => 0.75, _ => 0.50 },
                    DivisionWeight = RatingCalculator.DivisionWeight(d.OpponentDivision)
                }).ToList();

                var baseSOS = withWeights
                    .GroupBy(w => w.TeamId)
                    .Select(g => new
                    {
                        TeamId  = g.Key,
                        BaseSOS = Math.Round(g.Sum(x => x.Weight * x.DivisionWeight) / g.Sum(x => x.DivisionWeight), 3)
                    }).ToList();

                var opponentSOS = withWeights
                    .Join(baseSOS, w => w.OpponentId, b => b.TeamId,
                        (w, b) => new { w.TeamId, OppBaseSOS = b.BaseSOS, w.Weight })
                    .ToList();

                var secondOrderSOS = opponentSOS
                    .GroupBy(o => o.TeamId)
                    .Select(g => new
                    {
                        TeamId = g.Key,
                        SubSOS = Math.Round(g.Sum(x => x.OppBaseSOS * x.Weight) / g.Sum(x => x.Weight), 3)
                    }).ToList();

                var combined = baseSOS
                    .GroupJoin(secondOrderSOS, b => b.TeamId, s => s.TeamId,
                        (b, s) => new
                        {
                            b.TeamId, b.BaseSOS,
                            SubSOS = s.FirstOrDefault()?.SubSOS ?? b.BaseSOS
                        })
                    .Select(c => new
                    {
                        c.TeamId, c.BaseSOS, c.SubSOS,
                        CombinedSOS = Math.Round((2 * c.BaseSOS + 3 * c.SubSOS) / 5, 4)
                    }).ToList();

                // ── Step 10: Persist ──────────────────────────────────────────────
                var teamRecordsToUpdate = await _uow.TeamRecords.GetByYearWithTeamsAsync(targetYear, token);

                foreach (var record in teamRecordsToUpdate)
                {
                    var sosData = combined.FirstOrDefault(c => c.TeamId == record.TeamID);
                    if (string.Equals(record.Teams?.Division, "FCS", StringComparison.OrdinalIgnoreCase))
                    {
                        record.BaseSOS = record.SubSOS = record.CombinedSOS = 0;
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

            var gameParticipants = await _uow.Games.GetGameParticipantsAsync(targetYear, token);

            var teamRecords  = await _uow.TeamRecords.GetByYearAsync(targetYear, token);
            var winsLookup   = teamRecords.ToDictionary(tr => tr.TeamID, tr => (int)tr.Wins);
            var lossesLookup = teamRecords.ToDictionary(tr => tr.TeamID, tr => (int)tr.Losses);

            var avgScoreDeltas   = await _uow.Lookups.GetAvgScoreDeltasAsync(token);
            var matchupHistories = await _uow.Lookups.GetMatchupHistoriesAsync(token);
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
                        var effectiveStDev = (double)asd.WeightedStdDev * RatingCalculator.RivalryVarianceMultiplier(rivalryTier);
                        zScore             = RatingCalculator.DampenZScore((delta - expected) / effectiveStDev);
                    }

                    return new
                    {
                        gp.TeamId, gp.TeamDivision,
                        ZScore         = zScore,
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

            var teamRecordsForUpdate = await _uow.TeamRecords.GetByYearWithTeamsAsync(targetYear, token);

            foreach (var record in teamRecordsForUpdate)
            {
                if (string.Equals(record.Teams?.Division, "FCS", StringComparison.OrdinalIgnoreCase)) { record.PowerRating = 0; continue; }

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
            var teamRecords = await _uow.TeamRecords.GetByYearWithTeamsAsync(targetYear, token);

            foreach (var record in teamRecords)
            {
                if (string.Equals(record.Teams?.Division, "FCS", StringComparison.OrdinalIgnoreCase)) { record.Ranking = 0; continue; }

                var totalGames = record.Wins + record.Losses;
                if (totalGames > 0 && record.CombinedSOS.HasValue && record.PowerRating.HasValue)
                {
                    var winPct = (decimal)record.Wins / totalGames;
                    record.Ranking = Math.Round(winPct * (1 + record.PowerRating.Value), 4);
                }
                else
                {
                    record.Ranking = 0;
                }
            }

            await _uow.SaveChangesAsync(token);
        }
    }
}
