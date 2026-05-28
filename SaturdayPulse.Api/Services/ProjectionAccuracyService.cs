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
            int? endYear   = null,
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
                SnapshotWeek     = g.BestProjection.Week,
                PredictedSpread  = (double)g.BestProjection.PredictedSpread,
                PredictedTotal   = (double)g.BestProjection.PredictedTotal,
                ActualMargin     = (double)(g.HomePoints - g.AwayPoints),
                ActualTotal      = (double)(g.HomePoints + g.AwayPoints),
                VegasSpread      = g.VegasSpread,
                VegasSpreadOpen  = g.VegasSpreadOpen,
                VegasTotal       = g.VegasTotal,
                SpreadError      = Math.Abs((double)g.BestProjection.PredictedSpread -
                                            (double)(g.HomePoints - g.AwayPoints)),
                TotalError       = Math.Abs((double)g.BestProjection.PredictedTotal -
                                            (double)(g.HomePoints + g.AwayPoints)),
                VegasError       = g.VegasSpread.HasValue
                    ? Math.Abs((double)g.VegasSpread.Value - (double)(g.HomePoints - g.AwayPoints))
                    : (double?)null,
                VegasOpenError   = g.VegasSpreadOpen.HasValue
                    ? Math.Abs((double)g.VegasSpreadOpen.Value - (double)(g.HomePoints - g.AwayPoints))
                    : (double?)null,
                VegasTotalError  = g.VegasTotal.HasValue
                    ? Math.Abs((double)g.VegasTotal.Value - (double)(g.HomePoints + g.AwayPoints))
                    : (double?)null,
                CorrectWinner    = Math.Sign((double)g.BestProjection.PredictedSpread) ==
                                    Math.Sign((double)(g.HomePoints - g.AwayPoints))
            }).ToList();

            var totalGames = gameResults.Count;

            // ── Core metrics ──────────────────────────────────────────────────────
            var mae            = gameResults.Average(g => g.SpreadError);
            var totalMae       = gameResults.Average(g => g.TotalError);
            var winnerAccuracy = (double)gameResults.Count(g => g.CorrectWinner) / totalGames * 100.0;
            var spreadBias     = gameResults.Average(g => g.PredictedSpread - g.ActualMargin);
            var totalBias      = gameResults.Average(g => g.PredictedTotal  - g.ActualTotal);

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
            double? vegasTotalMae  = vegasTotalGames.Any()
                ? vegasTotalGames.Average(g => g.VegasTotalError!.Value) : null;
            double? vegasTotalBias = vegasTotalGames.Any()
                ? vegasTotalGames.Average(g => (double)g.VegasTotal!.Value - g.ActualTotal) : null;

            // ── By snapshot week ──────────────────────────────────────────────────
            var byWeek = gameResults
                .GroupBy(g => g.SnapshotWeek)
                .Select(grp => new AccuracyByWeek(
                    SnapshotWeek:   grp.Key,
                    Games:          grp.Count(),
                    MAE:            Math.Round(grp.Average(g => g.SpreadError), 2),
                    WinnerAccuracy: Math.Round(
                        (double)grp.Count(g => g.CorrectWinner) / grp.Count() * 100.0, 1)))
                .OrderBy(x => x.SnapshotWeek)
                .ToList();

            // ── By year ───────────────────────────────────────────────────────────
            var byYear = gameResults
                .GroupBy(g => g.Year)
                .Select(grp => new AccuracyByYear(
                    Year:           grp.Key,
                    Games:          grp.Count(),
                    MAE:            Math.Round(grp.Average(g => g.SpreadError), 2),
                    WinnerAccuracy: Math.Round(
                        (double)grp.Count(g => g.CorrectWinner) / grp.Count() * 100.0, 1),
                    SpreadBias:     Math.Round(
                        grp.Average(g => g.PredictedSpread - g.ActualMargin), 2)))
                .OrderBy(x => x.Year)
                .ToList();

            // ── By conference tier ────────────────────────────────────────────────
            var byConference = gameResults
                .Where(g => g.ConferenceTier != null && g.ConferenceTier != "Other")
                .GroupBy(g => g.ConferenceTier!)
                .Select(grp => new AccuracyByConference(
                    Conference:     grp.Key,
                    Games:          grp.Count(),
                    MAE:            Math.Round(grp.Average(g => g.SpreadError), 2),
                    WinnerAccuracy: Math.Round(
                        (double)grp.Count(g => g.CorrectWinner) / grp.Count() * 100.0, 1)))
                .OrderBy(x => x.MAE)
                .ToList();

            return new ProjectionAccuracyResult(
                StartYear:               startYear ?? gameResults.Min(g => g.Year),
                EndYear:                 endYear   ?? gameResults.Max(g => g.Year),
                TotalGames:              totalGames,
                MAE:                     Math.Round(mae,            2),
                TotalMAE:                Math.Round(totalMae,       2),
                WinnerAccuracyPct:       Math.Round(winnerAccuracy, 1),
                SpreadBias:              Math.Round(spreadBias,     2),
                TotalBias:               Math.Round(totalBias,      2),
                VegasMAE:                vegasMae.HasValue              ? Math.Round(vegasMae.Value,                2) : null,
                VegasWinnerAccuracy:     vegasWinnerAccuracy.HasValue   ? Math.Round(vegasWinnerAccuracy.Value,    1) : null,
                VegasGames:              vegasGames.Count,
                VegasOpenMAE:            vegasOpenMae.HasValue          ? Math.Round(vegasOpenMae.Value,           2) : null,
                VegasOpenWinnerAccuracy: vegasOpenWinnerAccuracy.HasValue
                                             ? Math.Round(vegasOpenWinnerAccuracy.Value, 1) : null,
                VegasOpenGames:          vegasOpenGames.Count,
                VegasTotalMAE:           vegasTotalMae.HasValue         ? Math.Round(vegasTotalMae.Value,          2) : null,
                VegasTotalBias:          vegasTotalBias.HasValue        ? Math.Round(vegasTotalBias.Value,         2) : null,
                ByWeek:                  byWeek,
                ByYear:                  byYear,
                ByConference:            byConference);
        }

        // ── Private data loader ───────────────────────────────────────────────────

        private async Task<List<GameProjectionData>> LoadGamesWithProjectionsAsync(
            int? startYear, int? endYear, CancellationToken token)
        {
            // Load played games in range.
            var gamesQuery = _context.Games
                .Where(g => g.HomePoints != null && g.HomePoints > 0);
            if (startYear.HasValue) gamesQuery = gamesQuery.Where(g => g.Year >= startYear.Value);
            if (endYear.HasValue)   gamesQuery = gamesQuery.Where(g => g.Year <= endYear.Value);
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
                    var spreads     = g.Where(l => l.Spread.HasValue)
                                       .Select(l => -(double)l.Spread!.Value).ToList();
                    var spreadsOpen = g.Where(l => l.SpreadOpen.HasValue)
                                       .Select(l => -(double)l.SpreadOpen!.Value).ToList();
                    var overUnders  = g.Where(l => l.OverUnder.HasValue)
                                       .Select(l => (double)l.OverUnder!.Value).ToList();
                    return (
                        Spread:     spreads.Any()     ? (decimal?)spreads.Average()     : null,
                        SpreadOpen: spreadsOpen.Any() ? (decimal?)spreadsOpen.Average() : null,
                        OverUnder:  overUnders.Any()  ? (decimal?)overUnders.Average()  : null
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
                        Year            = g.Year,
                        Week            = g.Week,
                        HomePoints      = g.HomePoints ?? 0,
                        AwayPoints      = g.AwayPoints ?? 0,
                        ConferenceTier  = g.HomeId.HasValue
                            ? tierByTeam.GetValueOrDefault(g.HomeId.Value)
                            : null,
                        BestProjection  = projByGame[g.GameId]!,
                        VegasSpread     = line.Spread,
                        VegasSpreadOpen = line.SpreadOpen,
                        VegasTotal      = line.OverUnder
                    };
                })
                .ToList();
        }

        // ── Conference tier mapping ───────────────────────────────────────────────
        // Uses the conference Name field (which stores abbreviations in this DB)
        // joined via the correct Conferences.ConferenceId FK.

        private static string GetConferenceTier(string? name, string? teamName = null)
        {
            if (teamName?.Equals("Notre Dame",  StringComparison.OrdinalIgnoreCase) == true) return "P4";
            if (teamName?.Equals("Connecticut", StringComparison.OrdinalIgnoreCase) == true) return "G5";

            return name switch
            {
                "SEC"            => "P4",
                "Big Ten"        => "P4",
                "Big 12"         => "P4",
                "ACC"            => "P4",
                "American Athletic" => "G5",
                "Mountain West"  => "G5",
                "Sun Belt"       => "G5",
                "Mid-American"   => "G5",
                "Conference USA" => "G5",
                "FBS Independents" => "Independent",
                _                => "Other"
            };
        }
    }
}
