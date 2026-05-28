using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Api.Contracts.Responses;
using SaturdayPulse.Data;
using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Computes projection accuracy metrics by comparing stored Projections
    /// against actual game results in the Games table.
    ///
    /// Uses direct NCAAContext access for the cross-table query that joins
    /// Games, Projections, Lines, Teams, and Conferences — this query doesn't
    /// belong in any single repository since it spans multiple aggregates.
    ///
    /// Metrics:
    ///   MAE                  — Mean Absolute Error on predicted spread vs actual margin
    ///   TotalMAE             — Mean Absolute Error on predicted total vs actual total
    ///   WinnerAccuracy       — % of games where we correctly predicted the winner
    ///   SpreadBias           — systematic over/under prediction (positive = favor home)
    ///   TotalBias            — systematic over/under on game totals
    ///   VegasMAE             — Vegas closing line MAE
    ///   VegasOpenMAE         — Vegas opening line MAE (true apples-to-apples vs our model)
    ///   VegasTotalMAE        — Vegas over/under MAE
    ///   VegasTotalBias       — Vegas systematic bias on totals
    ///   ByWeek               — MAE by snapshot week (accuracy improves closer to game)
    ///   ByYear               — year-over-year accuracy trend
    ///   ByConference         — accuracy by conference tier (P4 / G5 / Independent)
    /// </summary>
    public class ProjectionAccuracyService
    {
        private readonly NCAAContext _context;

        public ProjectionAccuracyService(NCAAContext context) => _context = context;

        public async Task<ProjectionAccuracyResult> ComputeAccuracyAsync(
            int? startYear = null,
            int? endYear = null,
            CancellationToken token = default)
        {
            var games = await LoadGamesWithProjectionsAsync(startYear, endYear, token);

            if (!games.Any())
                return ProjectionAccuracyResult.Empty(startYear, endYear);

            var gameResults = games.Select(g => new
            {
                g.Year,
                g.Week,
                g.ConferenceTier,
                SnapshotWeek = g.BestProjection.Week,
                PredictedSpread = (double)g.BestProjection.PredictedSpread,
                PredictedTotal = (double)g.BestProjection.PredictedTotal,
                ActualMargin = (double)(g.HomePoints - g.AwayPoints),
                ActualTotal = (double)(g.HomePoints + g.AwayPoints),
                VegasSpread = g.VegasSpread,
                VegasSpreadOpen = g.VegasSpreadOpen,
                VegasTotal = g.VegasTotal,
                SpreadError = Math.Abs((double)g.BestProjection.PredictedSpread -
                                            (double)(g.HomePoints - g.AwayPoints)),
                TotalError = Math.Abs((double)g.BestProjection.PredictedTotal -
                                            (double)(g.HomePoints + g.AwayPoints)),
                VegasError = g.VegasSpread.HasValue
                    ? Math.Abs((double)g.VegasSpread.Value - (double)(g.HomePoints - g.AwayPoints))
                    : (double?)null,
                VegasOpenError = g.VegasSpreadOpen.HasValue
                    ? Math.Abs((double)g.VegasSpreadOpen.Value - (double)(g.HomePoints - g.AwayPoints))
                    : (double?)null,
                VegasTotalError = g.VegasTotal.HasValue
                    ? Math.Abs((double)g.VegasTotal.Value - (double)(g.HomePoints + g.AwayPoints))
                    : (double?)null,
                CorrectWinner = Math.Sign((double)g.BestProjection.PredictedSpread) ==
                                    Math.Sign((double)(g.HomePoints - g.AwayPoints))
            }).ToList();

            var totalGames = gameResults.Count;

            // ── Core metrics ──────────────────────────────────────────────────────
            var mae = gameResults.Average(g => g.SpreadError);
            var totalMae = gameResults.Average(g => g.TotalError);
            var winnerAccuracy = (double)gameResults.Count(g => g.CorrectWinner) / totalGames * 100.0;
            var spreadBias = gameResults.Average(g => g.PredictedSpread - g.ActualMargin);
            var totalBias = gameResults.Average(g => g.PredictedTotal - g.ActualTotal);

            // ── Vegas closing line ────────────────────────────────────────────────
            var vegasGames = gameResults.Where(g => g.VegasError.HasValue).ToList();
            double? vegasMae = vegasGames.Any() ? vegasGames.Average(g => g.VegasError!.Value) : null;
            double? vegasWinnerAccuracy = vegasGames.Any()
                ? (double)vegasGames.Count(g =>
                    Math.Sign((double)g.VegasSpread!.Value) == Math.Sign(g.ActualMargin)) /
                  vegasGames.Count * 100.0
                : null;

            // ── Vegas opening line ────────────────────────────────────────────────
            var vegasOpenGames = gameResults.Where(g => g.VegasOpenError.HasValue).ToList();
            double? vegasOpenMae = vegasOpenGames.Any()
                ? vegasOpenGames.Average(g => g.VegasOpenError!.Value) : null;
            double? vegasOpenWinnerAccuracy = vegasOpenGames.Any()
                ? (double)vegasOpenGames.Count(g =>
                    Math.Sign((double)g.VegasSpreadOpen!.Value) == Math.Sign(g.ActualMargin)) /
                  vegasOpenGames.Count * 100.0
                : null;

            // ── Vegas over/under ──────────────────────────────────────────────────
            var vegasTotalGames = gameResults.Where(g => g.VegasTotalError.HasValue).ToList();
            double? vegasTotalMae = vegasTotalGames.Any()
                ? vegasTotalGames.Average(g => g.VegasTotalError!.Value) : null;
            double? vegasTotalBias = vegasTotalGames.Any()
                ? vegasTotalGames.Average(g => (double)g.VegasTotal!.Value - g.ActualTotal) : null;

            // ── By snapshot week ──────────────────────────────────────────────────
            var byWeek = gameResults
                .GroupBy(g => g.SnapshotWeek)
                .Select(grp => new AccuracyByWeek(
                    SnapshotWeek: grp.Key,
                    Games: grp.Count(),
                    MAE: Math.Round(grp.Average(g => g.SpreadError), 2),
                    WinnerAccuracy: Math.Round(
                        (double)grp.Count(g => g.CorrectWinner) / grp.Count() * 100.0, 1)))
                .OrderBy(x => x.SnapshotWeek)
                .ToList();

            // ── By year ───────────────────────────────────────────────────────────
            var byYear = gameResults
                .GroupBy(g => g.Year)
                .Select(grp => new AccuracyByYear(
                    Year: grp.Key,
                    Games: grp.Count(),
                    MAE: Math.Round(grp.Average(g => g.SpreadError), 2),
                    WinnerAccuracy: Math.Round(
                        (double)grp.Count(g => g.CorrectWinner) / grp.Count() * 100.0, 1),
                    SpreadBias: Math.Round(
                        grp.Average(g => g.PredictedSpread - g.ActualMargin), 2)))
                .OrderBy(x => x.Year)
                .ToList();

            // ── By conference tier ────────────────────────────────────────────────
            var byConference = gameResults
                .Where(g => g.ConferenceTier != null && g.ConferenceTier != "Other")
                .GroupBy(g => g.ConferenceTier!)
                .Select(grp => new AccuracyByConference(
                    Conference: grp.Key,
                    Games: grp.Count(),
                    MAE: Math.Round(grp.Average(g => g.SpreadError), 2),
                    WinnerAccuracy: Math.Round(
                        (double)grp.Count(g => g.CorrectWinner) / grp.Count() * 100.0, 1)))
                .OrderBy(x => x.MAE)
                .ToList();

            // ── By season phase ───────────────────────────────────────────────────────
            var byPhase = gameResults
                .GroupBy(g => g.Week switch
                {
                    <= 15 => "Regular Season",
                    <= 17 => "Conference Championships",
                    18 => "Bowl Games",
                    _ => "Playoffs"
                })
                .Select(grp => new AccuracyByPhase(
                    Phase: grp.Key,
                    Games: grp.Count(),
                    MAE: Math.Round(grp.Average(g => g.SpreadError), 2),
                    WinnerAccuracy: Math.Round(
                        (double)grp.Count(g => g.CorrectWinner) / grp.Count() * 100.0, 1),
                    VegasMAE: grp.Any(g => g.VegasError.HasValue)
                        ? Math.Round(grp.Where(g => g.VegasError.HasValue)
                                       .Average(g => g.VegasError!.Value), 2)
                        : (double?)null))
                .OrderBy(x => x.Phase switch
                {
                    "Regular Season" => 1,
                    "Conference Championships" => 2,
                    "Bowl Games" => 3,
                    "Playoffs" => 4,
                    _ => 5
                })
                .ToList();

            return new ProjectionAccuracyResult(
                StartYear: startYear ?? gameResults.Min(g => g.Year),
                EndYear: endYear ?? gameResults.Max(g => g.Year),
                TotalGames: totalGames,
                MAE: Math.Round(mae, 2),
                TotalMAE: Math.Round(totalMae, 2),
                WinnerAccuracyPct: Math.Round(winnerAccuracy, 1),
                SpreadBias: Math.Round(spreadBias, 2),
                TotalBias: Math.Round(totalBias, 2),
                VegasMAE: vegasMae.HasValue ? Math.Round(vegasMae.Value, 2) : null,
                VegasWinnerAccuracy: vegasWinnerAccuracy.HasValue ? Math.Round(vegasWinnerAccuracy.Value, 1) : null,
                VegasGames: vegasGames.Count,
                VegasOpenMAE: vegasOpenMae.HasValue ? Math.Round(vegasOpenMae.Value, 2) : null,
                VegasOpenWinnerAccuracy: vegasOpenWinnerAccuracy.HasValue
                                             ? Math.Round(vegasOpenWinnerAccuracy.Value, 1) : null,
                VegasOpenGames: vegasOpenGames.Count,
                VegasTotalMAE: vegasTotalMae.HasValue ? Math.Round(vegasTotalMae.Value, 2) : null,
                VegasTotalBias: vegasTotalBias.HasValue ? Math.Round(vegasTotalBias.Value, 2) : null,
                ByWeek: byWeek,
                ByYear: byYear,
                ByConference: byConference,
                ByPhase: byPhase);
        }

        // ── Private data loader ───────────────────────────────────────────────────

        private async Task<List<GameProjectionData>> LoadGamesWithProjectionsAsync(
            int? startYear, int? endYear, CancellationToken token)
        {
            // Load played games in range.
            var gamesQuery = _context.Games
                .Where(g => g.HomePoints != null && g.HomePoints > 0);
            if (startYear.HasValue) gamesQuery = gamesQuery.Where(g => g.Year >= startYear.Value);
            if (endYear.HasValue) gamesQuery = gamesQuery.Where(g => g.Year <= endYear.Value);
            var games = await gamesQuery.ToListAsync(token);

            if (!games.Any()) return new List<GameProjectionData>();

            var gameIds = games.Select(g => g.GameId).ToList();

            // Load projections — pick freshest snapshot before game week.
            var allProjections = await _context.Projections
                .Where(p => gameIds.Contains(p.GameId))
                .ToListAsync(token);

            var projByGame = allProjections
                .GroupBy(p => p.GameId)
                .ToDictionary(g => g.Key, g =>
                {
                    var gameWeek = games.First(gm => gm.GameId == g.Key).Week;
                    return g.Where(p => p.Week < gameWeek)
                             .OrderByDescending(p => p.Week)
                             .FirstOrDefault();
                });

            // Average closing spread, opening spread, and over/under across all books.
            // Flip sign — Lines stored as away perspective, we use home perspective.
            var lines = await _context.Lines
                .Where(l => gameIds.Contains(l.GameId))
                .ToListAsync(token);

            var lineByGame = lines
                .GroupBy(l => l.GameId)
                .ToDictionary(g => g.Key, g =>
                {
                    var spreads = g.Where(l => l.Spread.HasValue)
                                       .Select(l => -(double)l.Spread!.Value).ToList();
                    var spreadsOpen = g.Where(l => l.SpreadOpen.HasValue)
                                       .Select(l => -(double)l.SpreadOpen!.Value).ToList();
                    var overUnders = g.Where(l => l.OverUnder.HasValue)
                                       .Select(l => (double)l.OverUnder!.Value).ToList();
                    return (
                        Spread: spreads.Any() ? (decimal?)spreads.Average() : null,
                        SpreadOpen: spreadsOpen.Any() ? (decimal?)spreadsOpen.Average() : null,
                        OverUnder: overUnders.Any() ? (decimal?)overUnders.Average() : null
                    );
                });

            // Load conference tiers for home teams via correct FK join.
            // Teams.ConferenceId → Conferences.ConferenceId (not Conferences.Id)
            var teamIds = games
                .SelectMany(g => new[] { g.HomeId ?? 0 })
                .Where(id => id > 0).Distinct().ToList();

            var tierByTeam = await _context.Teams
                .Where(t => teamIds.Contains(t.TeamId))
                .Join(_context.Conferences,
                    t => t.ConferenceId,
                    c => c.ConferenceId,
                    (t, c) => new { t.TeamId, t.TeamName, c.Name })
                .ToDictionaryAsync(x => x.TeamId,
                    x => GetConferenceTier(x.Name, x.TeamName),
                    token);

            return games
                .Where(g => projByGame.GetValueOrDefault(g.GameId) != null)
                .Select(g =>
                {
                    lineByGame.TryGetValue(g.GameId, out var line);
                    return new GameProjectionData
                    {
                        HomeTeamId = g.HomeId,
                        AwayTeamId = g.AwayId,
                        Year = g.Year,
                        Week = g.Week,
                        HomePoints = g.HomePoints ?? 0,
                        AwayPoints = g.AwayPoints ?? 0,
                        ConferenceTier = g.HomeId.HasValue
                            ? tierByTeam.GetValueOrDefault(g.HomeId.Value)
                            : null,
                        BestProjection = projByGame[g.GameId]!,
                        VegasSpread = line.Spread,
                        VegasSpreadOpen = line.SpreadOpen,
                        VegasTotal = line.OverUnder
                    };
                })
                .ToList();
        }

        // ── Conference tier mapping ───────────────────────────────────────────────
        // Uses the conference Name field (which stores abbreviations in this DB)
        // joined via the correct Conferences.ConferenceId FK.

        private static string GetConferenceTier(string? name, string? teamName = null)
        {
            if (teamName?.Equals("Notre Dame", StringComparison.OrdinalIgnoreCase) == true) return "P4";
            if (teamName?.Equals("Connecticut", StringComparison.OrdinalIgnoreCase) == true) return "G5";

            return name switch
            {
                "SEC" => "P4",
                "Big Ten" => "P4",
                "Big 12" => "P4",
                "ACC" => "P4",
                "American Athletic" => "G5",
                "Mountain West" => "G5",
                "Sun Belt" => "G5",
                "Mid-American" => "G5",
                "Conference USA" => "G5",
                "FBS Independents" => "Independent",
                _ => "Other"
            };
        }

        public async Task<PortalAccuracyResult> ComputePortalAccuracyAsync(
        int? startYear = null,
        int? endYear = null,
        CancellationToken token = default)
        {
            var games = await LoadGamesWithProjectionsAsync(startYear, endYear, token);
            if (!games.Any()) return PortalAccuracyResult.Empty(startYear, endYear);

            // Load portal metrics for home teams
            var yearRange = games.Select(g => g.Year).Distinct().ToList();
            var portalRecords = await _context.TeamRecords
                .Where(tr => yearRange.Contains(tr.Year) && tr.PortalDelta != null)
                .ToDictionaryAsync(tr => (tr.TeamID, (int)tr.Year), token);

            var gameResults = games.Select(g =>
            {
                portalRecords.TryGetValue(((int)g.HomeTeamId, g.Year), out var tr);
                return new
                {
                    g.Year,
                    g.Week,
                    SnapshotWeek = g.BestProjection.Week,
                    ActualMargin = (double)(g.HomePoints - g.AwayPoints),
                    SpreadError = Math.Abs((double)g.BestProjection.PredictedSpread -
                                               (double)(g.HomePoints - g.AwayPoints)),
                    CorrectWinner = Math.Sign((double)g.BestProjection.PredictedSpread) ==
                                       Math.Sign((double)(g.HomePoints - g.AwayPoints)),
                    PortalDelta = tr?.PortalDelta,
                    TrendRating = tr?.TrendRating,
                    PortalGroup = tr?.PortalDelta switch
                    {
                        >= 1.5m => "Portal Winner",
                        <= -1.5m => "Portal Loser",
                        _ => "Neutral"
                    }
                };
            }).ToList();

            // ── By portal group and season period ────────────────────────────────────
            var byPortalGroup = gameResults
                .Where(g => g.PortalGroup != "Neutral")
                .GroupBy(g => new
                {
                    g.PortalGroup,
                    Period = g.SnapshotWeek switch
                    {
                        <= 3 => "Early (0-3)",
                        <= 8 => "Mid (4-8)",
                        _ => "Late (9+)"
                    }
                })
                .Select(grp => new PortalAccuracyByGroup(
                    PortalGroup: grp.Key.PortalGroup,
                    Period: grp.Key.Period,
                    Games: grp.Count(),
                    MAE: Math.Round(grp.Average(g => g.SpreadError), 2),
                    WinnerAccuracy: Math.Round(
                        (double)grp.Count(g => g.CorrectWinner) / grp.Count() * 100.0, 1)))
                .OrderBy(x => x.PortalGroup)
                .ThenBy(x => x.Period)
                .ToList();

            // ── Portal winner early vs late MAE gap ───────────────────────────────────
            var winnerEarly = byPortalGroup
                .FirstOrDefault(x => x.PortalGroup == "Portal Winner" && x.Period == "Early (0-3)");
            var winnerLate = byPortalGroup
                .FirstOrDefault(x => x.PortalGroup == "Portal Winner" && x.Period == "Late (9+)");
            var loserEarly = byPortalGroup
                .FirstOrDefault(x => x.PortalGroup == "Portal Loser" && x.Period == "Early (0-3)");
            var loserLate = byPortalGroup
                .FirstOrDefault(x => x.PortalGroup == "Portal Loser" && x.Period == "Late (9+)");

            return new PortalAccuracyResult(
                StartYear: startYear ?? games.Min(g => g.Year),
                EndYear: endYear ?? games.Max(g => g.Year),
                TotalGames: gameResults.Count,
                ByPortalGroup: byPortalGroup,
                WinnerEarlyMAE: winnerEarly?.MAE,
                WinnerLateMAE: winnerLate?.MAE,
                WinnerMAEGap: winnerEarly != null && winnerLate != null
                                        ? Math.Round(winnerEarly.MAE - winnerLate.MAE, 2) : null,
                LoserEarlyMAE: loserEarly?.MAE,
                LoserLateMAE: loserLate?.MAE,
                LoserMAEGap: loserEarly != null && loserLate != null
                                        ? Math.Round(loserEarly.MAE - loserLate.MAE, 2) : null);
        }


        public async Task<PortalWeightSimulationResult> SimulatePortalWeightsAsync(
            int? startYear = null,
            int? endYear = null,
            CancellationToken token = default)
        {
            var games = await LoadGamesWithProjectionsAsync(startYear, endYear, token);
            if (!games.Any()) return PortalWeightSimulationResult.Empty(startYear, endYear);

            // Load week 0 WeeklyRankings and TeamRecords for portal metrics
            var yearRange = games.Select(g => g.Year).Distinct().ToList();

            var week0Rankings = await _context.WeeklyRankings
                .Where(wr => yearRange.Contains(wr.Year) && wr.Week == 0)
                .ToDictionaryAsync(wr => (wr.TeamID, (int)wr.Year), token);

            var portalRecords = await _context.TeamRecords
                .Where(tr => yearRange.Contains(tr.Year) && tr.PortalDelta != null)
                .ToDictionaryAsync(tr => (tr.TeamID, (int)tr.Year), token);

            var leagueAvgTrend = await _context.TeamRecords
                .Where(tr => yearRange.Contains(tr.Year) && tr.TrendRating > 0)
                .AverageAsync(tr => (double?)tr.TrendRating, token) ?? 0.4953;

            // Only look at early season games (snapshot weeks 0-3) for portal-active teams
            var earlyGames = games
                .Where(g => g.BestProjection.Week <= 3 &&
                            g.HomeTeamId.HasValue)
                .Select(g =>
                {
                    portalRecords.TryGetValue((g.HomeTeamId ?? 0, g.Year), out var tr);
                    week0Rankings.TryGetValue((g.HomeTeamId ?? 0, g.Year), out var wr);

                    // Also get away team
                    portalRecords.TryGetValue((g.AwayTeamId ?? 0, g.Year), out var trAway);
                    week0Rankings.TryGetValue((g.AwayTeamId ?? 0, g.Year), out var wrAway);

                    return new
                    {
                        g.Year,
                        g.Week,
                        ActualMargin = (double)(g.HomePoints - g.AwayPoints),
                        OriginalSpread = (double)g.BestProjection.PredictedSpread,
                        HomePowerRating = wr?.PowerRating ?? 0m,
                        AwayPowerRating = wrAway?.PowerRating ?? 0m,
                        HomePortalDelta = tr?.PortalDelta ?? 0m,
                        AwayPortalDelta = trAway?.PortalDelta ?? 0m,
                        HomeTrendRating = tr?.TrendRating,
                        AwayTrendRating = trAway?.TrendRating,
                        PortalGroup = tr?.PortalDelta switch
                        {
                            >= 1.5m => "Portal Winner",
                            <= -1.5m => "Portal Loser",
                            _ => "Neutral"
                        }
                    };
                })
                .ToList();

            // Weight combinations to test
            var portalWeights = new[] { 0.02, 0.04, 0.06, 0.08, 0.10 };
            var trendWeights = new[] { 0.00, 0.05, 0.10, 0.15 };

            var results = new List<WeightSimulationRow>();

            foreach (var pw in portalWeights)
            {
                foreach (var tw in trendWeights)
                {
                    var simGames = earlyGames.Select(g =>
                    {
                        // Compute adjusted power ratings for both teams
                        var homeTrend = (double)(g.HomeTrendRating ?? (decimal)leagueAvgTrend);
                        var awayTrend = (double)(g.AwayTrendRating ?? (decimal)leagueAvgTrend);

                        var homeAdj = (double)g.HomePortalDelta * pw +
                                      (homeTrend - leagueAvgTrend) * tw;
                        var awayAdj = (double)g.AwayPortalDelta * pw +
                                      (awayTrend - leagueAvgTrend) * tw;

                        // Adjust spread by power rating differential change × 10
                        // (same scale factor used in GamePredictionService)
                        var spreadAdjustment = (homeAdj - awayAdj) * 10.0;
                        var simulatedSpread = g.OriginalSpread + spreadAdjustment;

                        var spreadError = Math.Abs(simulatedSpread - g.ActualMargin);
                        var correctWinner = Math.Sign(simulatedSpread) == Math.Sign(g.ActualMargin);

                        return new { g.PortalGroup, spreadError, correctWinner, simulatedSpread };
                    }).ToList();

                    // Overall early season metrics
                    var allGames = simGames.Count;
                    var overallMAE = simGames.Average(g => g.spreadError);
                    var overallWinner = (double)simGames.Count(g => g.correctWinner) / allGames * 100.0;

                    // Portal winner specific
                    var winnerGames = simGames.Where(g => g.PortalGroup == "Portal Winner").ToList();
                    var winnerMAE = winnerGames.Any() ? winnerGames.Average(g => g.spreadError) : 0.0;
                    var winnerWinner = winnerGames.Any()
                        ? (double)winnerGames.Count(g => g.correctWinner) / winnerGames.Count * 100.0 : 0.0;

                    // Portal loser specific
                    var loserGames = simGames.Where(g => g.PortalGroup == "Portal Loser").ToList();
                    var loserMAE = loserGames.Any() ? loserGames.Average(g => g.spreadError) : 0.0;
                    var loserWinner = loserGames.Any()
                        ? (double)loserGames.Count(g => g.correctWinner) / loserGames.Count * 100.0 : 0.0;

                    results.Add(new WeightSimulationRow(
                        PortalWeight: pw,
                        TrendWeight: tw,
                        OverallMAE: Math.Round(overallMAE, 2),
                        OverallWinnerPct: Math.Round(overallWinner, 1),
                        PortalWinnerMAE: Math.Round(winnerMAE, 2),
                        PortalWinnerAccuracy: Math.Round(winnerWinner, 1),
                        PortalLoserMAE: Math.Round(loserMAE, 2),
                        PortalLoserAccuracy: Math.Round(loserWinner, 1)));
                }
            }

            // Baseline — no adjustment
            var baseline = earlyGames.Select(g => new
            {
                g.PortalGroup,
                spreadError = Math.Abs(g.OriginalSpread - g.ActualMargin),
                correctWinner = Math.Sign(g.OriginalSpread) == Math.Sign(g.ActualMargin)
            }).ToList();

            return new PortalWeightSimulationResult(
                StartYear: startYear ?? games.Min(g => g.Year),
                EndYear: endYear ?? games.Max(g => g.Year),
                TotalEarlyGames: earlyGames.Count,
                BaselineMAE: Math.Round(baseline.Average(g => g.spreadError), 2),
                BaselineWinnerPct: Math.Round(
                    (double)baseline.Count(g => g.correctWinner) / baseline.Count * 100.0, 1),
                BaselinePortalWinnerMAE: Math.Round(
                    baseline.Where(g => g.PortalGroup == "Portal Winner")
                            .Average(g => g.spreadError), 2),
                BaselinePortalWinnerAccuracy: Math.Round(
                    (double)baseline.Where(g => g.PortalGroup == "Portal Winner")
                                    .Count(g => g.correctWinner) /
                    baseline.Count(g => g.PortalGroup == "Portal Winner") * 100.0, 1),
                Simulations: results.OrderBy(r => r.PortalWinnerMAE).ToList());
        }
    }
}
