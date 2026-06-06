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
    /// === Pipeline order (sequential) ===
    ///   RollingAverageService.ComputeAndPersistAsync
    ///     → SetSOS → CalculatePowerRatings → CalculateRankings
    ///
    /// === Conceptual model (corrected) ===
    /// SOS is a pure opponent-strength metric:
    ///   • BaseSOS    = weighted average of opponents' PREGAME Ranking
    ///                  (falls back to SeedRating, then 0 for FCS/unknown)
    ///   • SubSOS     = weighted average of opponents' BaseSOS values
    ///   • CombinedSOS = (2 * BaseSOS + 3 * SubSOS) / 5
    ///
    /// Quality-of-win does NOT go into SOS. It modifies the team's own
    /// z-score in CalculatePowerRatings via a smooth modifier:
    ///   qualityMod = clamp(1 + z * 0.25, 0.50, 1.50)
    ///
    /// FCS opponents contribute strength = 0 to your SOS (they pull your
    /// average down). Your z-score against an FCS opponent is computed
    /// normally — beating them ugly hurts your PowerRating, losing to them
    /// hurts even more.
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

        // ── CalculateTrend (unchanged) ────────────────────────────────────────────

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

        // ── SetSOS (corrected — pure opponent strength) ───────────────────────────

        public async Task SetSOS(int? year = null, int? week = null, CancellationToken token = default)
        {
            try
            {
                var targetYear = year ?? DateTime.Now.Year;
                var targetWeek = week ?? 0;

                // ── Load opponent-strength lookup sources ─────────────────────────
                // SeedRating is the preseason proxy and the week-1 fallback.
                // WeeklyRankings.Ranking is the pregame strength once available.
                var teamRecordsAll = await _uow.TeamRecords.GetByYearWithTeamsAsync(targetYear, token);
                var seedRatingByTeam = teamRecordsAll
                    .Where(tr => tr.SeedRating.HasValue)
                    .ToDictionary(tr => tr.TeamID, tr => (decimal)tr.SeedRating!.Value);

                var divisionByTeam = teamRecordsAll
                    .ToDictionary(tr => tr.TeamID, tr => tr.Teams?.Division ?? string.Empty);

                var allWeeklyRankings = await _uow.WeeklyRankings.GetByYearAsync(targetYear, token);
                var rankingsByTeamWeek = allWeeklyRankings
                    .ToDictionary(wr => (wr.TeamID, (int)wr.Week));

                // Pregame opponent strength lookup chain:
                //   WeeklyRankings[opponent, week-1].Ranking → SeedRating → 0
                // FCS opponents are explicitly treated as 0 (lowest possible tier).
                decimal LookupOpponentStrength(int opponentId, int gameWeek)
                {
                    if (divisionByTeam.TryGetValue(opponentId, out var div) &&
                        string.Equals(div, "FCS", StringComparison.OrdinalIgnoreCase))
                        return 0m;

                    var priorWk = Math.Max(gameWeek - 1, 0);
                    if (rankingsByTeamWeek.TryGetValue((opponentId, priorWk), out var wr) &&
                        wr.Ranking > 0m)
                        return (decimal)wr.Ranking;

                    if (seedRatingByTeam.TryGetValue(opponentId, out var seed))
                        return seed;

                    return 0m;
                }

                // ── Game participants for the year (skipped for week 0 — see below)
                var gameParticipants = await _uow.Games.GetGameParticipantsAsync(targetYear, token);

                // ── BaseSOS per team ──────────────────────────────────────────────
                //
                // Two paths:
                //
                //   Week 0 (preseason):
                //     No games played. Use each team's full scheduled slate of
                //     opponents and compute a projected SOS from opponent
                //     SeedRating values (falls through the lookup chain above).
                //
                //   Week 1+:
                //     Use only games already played (through targetWeek).
                //     Each game contributes its opponent's pregame strength,
                //     weighted by division weight.
                //
                List<(int TeamId, decimal BaseSOS)> baseSOS;

                if (targetWeek == 0)
                {
                    // Preseason — every scheduled opponent counts
                    baseSOS = gameParticipants
                        .GroupBy(gp => gp.TeamId)
                        .Select(g =>
                        {
                            var contributions = g.Select(gp => new
                            {
                                Strength       = (double)LookupOpponentStrength(gp.OpponentId, gp.Week),
                                DivisionWeight = RatingCalculator.DivisionWeight(gp.OpponentDivision)
                            }).ToList();

                            var weightSum   = contributions.Sum(c => c.DivisionWeight);
                            var weightedSum = contributions.Sum(c => c.Strength * c.DivisionWeight);
                            var avg         = weightSum > 0 ? weightedSum / weightSum : 0.0;
                            return (TeamId: g.Key, BaseSOS: (decimal)Math.Round(avg, 4));
                        })
                        .ToList();
                }
                else
                {
                    // Mid-season — only games played through targetWeek count
                    if (targetWeek < _config.SosWeekThreshold)
                    {
                        // Preliminary SOS still calculated below; threshold no longer
                        // bails out entirely so early weeks have meaningful values
                        // (uses SeedRating fallback in the lookup chain).
                    }

                    var playedGames = gameParticipants
                        .Where(gp => gp.Week <= targetWeek)
                        .ToList();

                    baseSOS = playedGames
                        .GroupBy(gp => gp.TeamId)
                        .Select(g =>
                        {
                            var contributions = g.Select(gp => new
                            {
                                Strength       = (double)LookupOpponentStrength(gp.OpponentId, gp.Week),
                                DivisionWeight = RatingCalculator.DivisionWeight(gp.OpponentDivision)
                            }).ToList();

                            var weightSum   = contributions.Sum(c => c.DivisionWeight);
                            var weightedSum = contributions.Sum(c => c.Strength * c.DivisionWeight);
                            var avg         = weightSum > 0 ? weightedSum / weightSum : 0.0;
                            return (TeamId: g.Key, BaseSOS: (decimal)Math.Round(avg, 4));
                        })
                        .ToList();
                }

                var baseSOSByTeam = baseSOS.ToDictionary(b => b.TeamId, b => b.BaseSOS);

                // ── SubSOS per team — average of opponents' BaseSOS ───────────────
                // Same opponent set used for BaseSOS (preseason: all scheduled;
                // mid-season: only games played through targetWeek), same
                // division weighting.
                var scopedGames = targetWeek == 0
                    ? gameParticipants
                    : gameParticipants.Where(gp => gp.Week <= targetWeek);

                var subSOS = scopedGames
                    .GroupBy(gp => gp.TeamId)
                    .Select(g =>
                    {
                        var contributions = g.Select(gp => new
                        {
                            OppBaseSOS     = baseSOSByTeam.TryGetValue(gp.OpponentId, out var b) ? b : 0m,
                            DivisionWeight = RatingCalculator.DivisionWeight(gp.OpponentDivision)
                        }).ToList();

                        var weightSum   = contributions.Sum(c => c.DivisionWeight);
                        var weightedSum = contributions.Sum(c => (double)c.OppBaseSOS * c.DivisionWeight);
                        var avg         = weightSum > 0 ? weightedSum / weightSum : 0.0;
                        return (TeamId: g.Key, SubSOS: (decimal)Math.Round(avg, 4));
                    })
                    .ToList();

                var subSOSByTeam = subSOS.ToDictionary(s => s.TeamId, s => s.SubSOS);

                // ── CombinedSOS — blend (2 * Base + 3 * Sub) / 5 ──────────────────
                var combined = baseSOS.Select(b =>
                {
                    var sub = subSOSByTeam.TryGetValue(b.TeamId, out var s) ? s : b.BaseSOS;
                    return new
                    {
                        b.TeamId,
                        BaseSOS     = b.BaseSOS,
                        SubSOS      = sub,
                        CombinedSOS = Math.Round((2 * b.BaseSOS + 3 * sub) / 5m, 4)
                    };
                }).ToList();

                // ── Persist ───────────────────────────────────────────────────────
                foreach (var record in teamRecordsAll)
                {
                    if (string.Equals(record.Teams?.Division, "FCS", StringComparison.OrdinalIgnoreCase))
                    {
                        record.BaseSOS = record.SubSOS = record.CombinedSOS = 0;
                        continue;
                    }

                    var sosData = combined.FirstOrDefault(c => c.TeamId == record.TeamID);
                    if (sosData != null)
                    {
                        record.BaseSOS     = sosData.BaseSOS;
                        record.SubSOS      = sosData.SubSOS;
                        record.CombinedSOS = sosData.CombinedSOS;
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

        // ── CalculatePowerRatings (corrected — quality-of-win lives here) ─────────

        public async Task CalculatePowerRatings(int? year = null, CancellationToken token = default)
        {
            var targetYear = year ?? DateTime.Now.Year;

            var gameParticipants = await _uow.Games.GetGameParticipantsAsync(targetYear, token);

            // Pregame strength lookup uses (TeamId, week-1) like before
            var allWeeklyRankings  = await _uow.WeeklyRankings.GetByYearAsync(targetYear, token);
            var rankingsByTeamWeek = allWeeklyRankings
                .ToDictionary(wr => (TeamId: wr.TeamID, Week: (int)wr.Week));

            var avgScoreDifferentials = await _uow.Lookups.GetAvgScoreDifferentialsAsync(token);
            var matchupHistories      = await _uow.Lookups.GetMatchupHistoriesAsync(token);
            var hfa                   = _config.HomeFieldAdvantage;

            // ── Smooth quality-of-win modifier ────────────────────────────────
            //   modifier = clamp(1 + z * 0.25, 0.50, 1.50)
            // Replaces the old four-bucket step function. Each game's z-score
            // produces a smooth multiplier between 0.50 (heavy under-performance)
            // and 1.50 (heavy over-performance). Multiplied directly into the
            // team's own z-score before averaging.
            static double QualityModifier(double z)
            {
                var raw = 1.0 + (z * 0.25);
                return Math.Max(0.50, Math.Min(1.50, raw));
            }

            var zScores = gameParticipants
                .Select(gp =>
                {
                    var priorWk = Math.Max(gp.Week - 1, 0);

                    rankingsByTeamWeek.TryGetValue((TeamId: gp.TeamId,     Week: priorWk), out var teamPrior);
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

                    // Smooth quality-of-win modifier applied here
                    var qualityMod = QualityModifier(zScore);

                    return new
                    {
                        gp.TeamId,
                        gp.TeamDivision,
                        WeightedZ      = zScore * qualityMod,
                        DivisionWeight = RatingCalculator.DivisionWeight(gp.OpponentDivision)
                    };
                })
                .GroupBy(x => x.TeamId)
                .Select(g => new
                {
                    TeamId       = g.Key,
                    TeamDivision = g.First().TeamDivision,
                    AvgZScore    = g.Sum(x => x.WeightedZ * x.DivisionWeight) / g.Sum(x => x.DivisionWeight)
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

        // ── CalculateRankings (unchanged for now — revisit after SOS settles) ────

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
