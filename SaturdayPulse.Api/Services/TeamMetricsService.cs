using Microsoft.Extensions.Options;
using SaturdayPulse.Configuration;
using SaturdayPulse.Contracts;
using System.Text.Json;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Manual recalculation endpoints for SOS, PowerRating, and Rankings.
    /// Used by admin endpoints when data needs to be recomputed outside the
    /// normal weekly WeeklyRankingsService pipeline.
    ///
    /// Z-score pipeline now uses AvgScoreDifferential (replaces AvgScoreDelta):
    ///   - Expected margin derived from ExpandStrength(prior week Ranking) differential
    ///   - StdDev from AvgScoreDifferential.StdDevMargin
    ///   - Consistent with WeeklyRankingsService and the prediction engine
    ///
    /// Pipeline order (must be sequential):
    ///   RollingAverageService.ComputeAndPersistAsync
    ///     → SetSOS → CalculatePowerRatings → CalculateRankings
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

                // ── Step 3: Load WeeklyRankings for pregame strength lookup ─────────
                // Index by (TeamId, Week) so each game uses the prior week's rating.
                var allWeeklyRankings  = await _uow.WeeklyRankings.GetByYearAsync(targetYear, token);
                var rankingsByTeamWeek = allWeeklyRankings
                    .ToDictionary(wr => (wr.TeamID, (int)wr.Week));

                // ── Step 4: Z-scores ──────────────────────────────────────────────
                var avgScoreDifferentials = await _uow.Lookups.GetAvgScoreDifferentialsAsync(token);
                var matchupHistories      = await _uow.Lookups.GetMatchupHistoriesAsync(token);
                var hfa                   = _config.HomeFieldAdvantage;

                var withDeltas = gameParticipants.Select(gp =>
                {
                    var delta    = gp.TeamPoints - gp.OpponentPoints;
                    var priorWk  = Math.Max(gp.Week - 1, 0);

                    rankingsByTeamWeek.TryGetValue((gp.TeamId,     priorWk), out var teamPrior);
                    rankingsByTeamWeek.TryGetValue((gp.OpponentId, priorWk), out var oppPrior);

                    var teamStrength = RatingCalculator.ExpandStrength(teamPrior?.Ranking ?? 0m);
                    var oppStrength  = RatingCalculator.ExpandStrength(oppPrior?.Ranking  ?? 0m);

                    var rawDiff      = teamStrength - oppStrength;
                    var clampedDiff  = Math.Max(-3.0m, Math.Min(3.0m, rawDiff));
                    var differential = Math.Round(clampedDiff / 0.05m, MidpointRounding.AwayFromZero) * 0.05m;

                    var bucketRow = avgScoreDifferentials
                        .OrderBy(b => Math.Abs(b.StrengthDifferential - differential))
                        .FirstOrDefault();

                    var rivalryTier = matchupHistories.FirstOrDefault(m =>
                        m.Team1Id == Math.Min(gp.TeamId, gp.OpponentId) &&
                        m.Team2Id == Math.Max(gp.TeamId, gp.OpponentId))?.RivalryTier;

                    double zValue = 0.0;
                    if (bucketRow != null && bucketRow.StdDevMargin != 0)
                    {
                        var expected = (double)RatingCalculator.GetSmoothedExpectedMargin(
                            avgScoreDifferentials, differential);
                        expected = RatingCalculator.ApplyHomeField(
                            expected, gp.IsHomeTeam, gp.Location == 'N', hfa);
                        var effectiveStDev = (double)bucketRow.StdDevMargin *
                            RatingCalculator.RivalryVarianceMultiplier(rivalryTier);
                        zValue = RatingCalculator.DampenZScore((delta - expected) / effectiveStDev);
                    }

                    return new
                    {
                        gp.TeamId, gp.TeamDivision, gp.OpponentId, gp.OpponentDivision,
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

            // Load all WeeklyRankings for the year — indexed by (TeamId, Week)
            // for per-game pregame strength lookup.
            var allWeeklyRankings  = await _uow.WeeklyRankings.GetByYearAsync(targetYear, token);
            var rankingsByTeamWeek = allWeeklyRankings
                .ToDictionary(wr => (TeamId: wr.TeamID, Week: (int)wr.Week));

            var avgScoreDifferentials = await _uow.Lookups.GetAvgScoreDifferentialsAsync(token);
            var matchupHistories      = await _uow.Lookups.GetMatchupHistoriesAsync(token);
            var hfa                   = _config.HomeFieldAdvantage;

            var zScores = gameParticipants
                .Select(gp =>
                {
                    var priorWk = Math.Max(gp.Week - 1, 0);

                    rankingsByTeamWeek.TryGetValue((TeamId: gp.TeamId, Week: priorWk), out var teamPrior);
                    rankingsByTeamWeek.TryGetValue((TeamId: gp.OpponentId, Week: priorWk), out var oppPrior);

                    var teamStrength = RatingCalculator.ExpandStrength(teamPrior?.Ranking ?? 0m);
                    var oppStrength  = RatingCalculator.ExpandStrength(oppPrior?.Ranking  ?? 0m);

                    var rawDiff      = teamStrength - oppStrength;
                    var clampedDiff  = Math.Max(-3.0m, Math.Min(3.0m, rawDiff));
                    var differential = Math.Round(clampedDiff / 0.05m, MidpointRounding.AwayFromZero) * 0.05m;

                    var bucketRow = avgScoreDifferentials
                        .OrderBy(b => Math.Abs(b.StrengthDifferential - differential))
                        .FirstOrDefault();

                    var rivalryTier = matchupHistories.FirstOrDefault(m =>
                        m.Team1Id == Math.Min(gp.TeamId, gp.OpponentId) &&
                        m.Team2Id == Math.Max(gp.TeamId, gp.OpponentId))?.RivalryTier;

                    double zScore = 0.0;
                    if (bucketRow != null && bucketRow.StdDevMargin != 0)
                    {
                        var expected = (double)RatingCalculator.GetSmoothedExpectedMargin(
                            avgScoreDifferentials, differential);
                        expected = RatingCalculator.ApplyHomeField(
                            expected, gp.IsHomeTeam, gp.Location == 'N', hfa);
                        var effectiveStDev = (double)bucketRow.StdDevMargin *
                            RatingCalculator.RivalryVarianceMultiplier(rivalryTier);
                        zScore = RatingCalculator.DampenZScore(
                            (gp.TeamPoints - gp.OpponentPoints - expected) / effectiveStDev);
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
                if (string.Equals(record.Teams?.Division, "FCS", StringComparison.OrdinalIgnoreCase))
                {
                    record.PowerRating = 0;
                    continue;
                }

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
                if (string.Equals(record.Teams?.Division, "FCS", StringComparison.OrdinalIgnoreCase))
                {
                    record.Ranking = 0;
                    continue;
                }

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
