using SaturdayPulse.Contracts;
using SaturdayPulse.Contracts.Requests;
using SaturdayPulse.Data;
using Microsoft.EntityFrameworkCore;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// EXPERIMENTAL — read-only diagnostic: runs both the production snapshot-cliff
    /// rating/prediction path and the experimental K=4 inertia blend for the same
    /// real games, and returns predicted spread and O/U side by side per game.
    /// Never persists anything — for manual/CSV review while calibrating K.
    ///
    /// Reuses GamePredictionService.BuildProjection (unchanged, already in production)
    /// to compute spread/total from each method's GamePrediction output, rather than
    /// reimplementing that math — keeps the comparison honest against exactly what
    /// production would have written to Projections.
    ///
    /// NEW FILE — part of the K=4 inertia-blending experimental comparison path.
    /// </summary>
    public class RatingComparisonService
    {
        private readonly GamePredictionService _production;
        private readonly ExperimentalInertiaRatingService _experimental;
        private readonly IUnitOfWork _uow;
        private readonly NCAAContext _context;

        public RatingComparisonService(
            GamePredictionService production,
            ExperimentalInertiaRatingService experimental,
            IUnitOfWork uow,
            NCAAContext context)
        {
            _production = production;
            _experimental = experimental;
            _uow = uow;
            _context = context;
        }

        /// <summary>
        /// Compares predicted spread/total for every real scheduled game in the given
        /// weeks, using production ratings vs. K=4-blended ratings. Includes games
        /// regardless of played status — this is about what each method WOULD have
        /// predicted, not about grading against actual results (that's a separate,
        /// follow-up analysis once you're happy with which method to trust).
        ///
        /// ADDED: hfaOverride — passed through to GamePredictionService.
        /// PredictMatchupsWithRatings for BOTH production and experimental ratings,
        /// so a candidate HFA value gets tested uniformly across both methods rather
        /// than favoring one. Null (default) behaves exactly as before.
        /// </summary>
        public async Task<List<RatingComparisonRow>> CompareAsync(
            int year, IEnumerable<int> weeks, double? hfaOverride = null, CancellationToken token = default)
        {
            var teamsDict = await _uow.Teams.GetDictionaryByTeamIdAsync(token);
            var allYearGames = await _uow.Games.GetByYearAsync(year, token);
            var rows = new List<RatingComparisonRow>();

            foreach (var week in weeks)
            {
                var weekGames = allYearGames
                    .Where(g => g.Week == week &&
                                g.HomeId.HasValue && g.AwayId.HasValue &&
                                teamsDict.ContainsKey(g.HomeId.Value) &&
                                teamsDict.ContainsKey(g.AwayId.Value))
                    .ToList();

                if (weekGames.Count == 0) continue;

                var matchupRequests = weekGames.Select(g => new MatchupRequest
                {
                    TeamName     = teamsDict[g.HomeId!.Value].TeamName,
                    OpponentName = teamsDict[g.AwayId!.Value].TeamName,
                    Location     = g.NeutralSite == true ? 'N' : 'H',
                    Week         = g.Week
                }).ToList();

                var prodRatings = await _production.GetProductionRatingsForComparisonAsync(year, week, token);
                var expRatings  = await _experimental.GetBlendedRatingsForWeekAsync(year, week, token);

                var prodPredictions = await _production.PredictMatchupsWithRatings(
                    year, prodRatings, matchupRequests, hfaOverride, token);
                var expPredictions  = await _production.PredictMatchupsWithRatings(
                    year, expRatings,  matchupRequests, hfaOverride, token);

                foreach (var g in weekGames)
                {
                    var homeTeam = teamsDict[g.HomeId!.Value];
                    var awayTeam = teamsDict[g.AwayId!.Value];

                    var prodPred = prodPredictions.FirstOrDefault(p =>
                        p.TeamName == homeTeam.TeamName &&
                        p.OpponentName == awayTeam.TeamName &&
                        p.Week == g.Week);
                    var expPred = expPredictions.FirstOrDefault(p =>
                        p.TeamName == homeTeam.TeamName &&
                        p.OpponentName == awayTeam.TeamName &&
                        p.Week == g.Week);

                    // Skip rather than fail — a missing prediction usually means one
                    // side had no rating record for one of the two teams that week
                    // (e.g. FCS opponent, see ExperimentalInertiaRatingService note).
                    if (prodPred == null || expPred == null) continue;

                    var prodProjection = GamePredictionService.BuildProjection(
                        prodPred, g.GameId, year, g.Week, g.HomeId.Value, g.AwayId.Value);
                    var expProjection = GamePredictionService.BuildProjection(
                        expPred, g.GameId, year, g.Week, g.HomeId.Value, g.AwayId.Value);

                    expRatings.TryGetValue(g.HomeId.Value, out var expHomeRecord);
                    expRatings.TryGetValue(g.AwayId.Value, out var expAwayRecord);

                    rows.Add(new RatingComparisonRow(
                        Year: year,
                        Week: g.Week,
                        GameId: g.GameId,
                        HomeTeamId: g.HomeId.Value,
                        HomeTeamName: homeTeam.TeamName,
                        AwayTeamId: g.AwayId.Value,
                        AwayTeamName: awayTeam.TeamName,
                        HomeGamesPlayed: (expHomeRecord?.Wins ?? 0) + (expHomeRecord?.Losses ?? 0),
                        AwayGamesPlayed: (expAwayRecord?.Wins ?? 0) + (expAwayRecord?.Losses ?? 0),
                        ProductionSpread: prodProjection.PredictedSpread,
                        ExperimentalSpread: expProjection.PredictedSpread,
                        SpreadDelta: expProjection.PredictedSpread - prodProjection.PredictedSpread,
                        ProductionTotal: prodProjection.PredictedTotal,
                        ExperimentalTotal: expProjection.PredictedTotal,
                        TotalDelta: expProjection.PredictedTotal - prodProjection.PredictedTotal));
                }
            }

            return rows
                .OrderBy(r => r.Week)
                .ThenByDescending(r => Math.Abs(r.SpreadDelta ?? 0))
                .ToList();
        }

        /// <summary>
        /// Grades both the production and experimental methods against actual final
        /// scores — the real question this project exists to answer ("is K=4 more
        /// accurate"), not just "do the two methods disagree" (that's CompareAsync).
        /// Reuses CompareAsync's per-game predictions rather than re-deriving them —
        /// only the actual-result lookup and MAE/bias math are new here.
        ///
        /// Metric definitions confirmed against ProjectionAccuracyService's actual
        /// source: MAE = Average(|predicted - actual|), bias = Average(predicted -
        /// actual), winner accuracy = Sign(predicted) == Sign(actual). Same math,
        /// just decimal here instead of double — no behavioral difference.
        ///
        /// ADDED: hfaOverride — passed through to CompareAsync, letting you test a
        /// candidate home-field-advantage constant against real results without
        /// redeploying. Null (default) behaves exactly as before.
        /// </summary>
        public async Task<RatingMethodAccuracyComparison> CompareAccuracyAsync(
            int year, IEnumerable<int> weeks, double? hfaOverride = null, CancellationToken token = default)
        {
            var weekList = weeks.ToList();
            var rows = await CompareAsync(year, weekList, hfaOverride: hfaOverride, token: token);

            // Confirmed via Games.cs: HomePoints/AwayPoints are genuinely nullable
            // (int?), not defaulting to 0 for unplayed games — HasValue is the
            // correct, direct check. (Earlier version of this used a `> 0` heuristic
            // to hedge against not having seen the entity; that hedge is gone now
            // that the actual schema is confirmed.)
            var yearGames = await _uow.Games.GetByYearAsync(year, token);
            var resultsByGameId = yearGames
                .Where(g => g.HomePoints.HasValue && g.AwayPoints.HasValue)
                .ToDictionary(
                    g => g.GameId,
                    g => (Margin: (decimal)(g.HomePoints!.Value - g.AwayPoints!.Value),
                          Total:  (decimal)(g.HomePoints!.Value + g.AwayPoints!.Value),
                          NeutralSite: g.NeutralSite));

            // Vegas lines — same join/convention as ProjectionAccuracyService.
            // Spread/SpreadOpen are stored from the AWAY team's perspective; negate
            // to match our own home-perspective PredictedSpread convention. Multiple
            // books per game get averaged. OverUnder is direction-agnostic, no flip.
            var gameIds = resultsByGameId.Keys.ToList();
            var lines = await _context.Lines
                .Where(l => gameIds.Contains(l.GameId))
                .ToListAsync(token);

            var lineByGame = lines
                .GroupBy(l => l.GameId)
                .ToDictionary(g => g.Key, g =>
                {
                    var spreads = g.Where(l => l.Spread.HasValue)
                        .Select(l => -(decimal)l.Spread!.Value).ToList();
                    var spreadsOpen = g.Where(l => l.SpreadOpen.HasValue)
                        .Select(l => -(decimal)l.SpreadOpen!.Value).ToList();
                    var overUnders = g.Where(l => l.OverUnder.HasValue)
                        .Select(l => (decimal)l.OverUnder!.Value).ToList();
                    return (
                        Spread: spreads.Count > 0 ? (decimal?)spreads.Average() : null,
                        SpreadOpen: spreadsOpen.Count > 0 ? (decimal?)spreadsOpen.Average() : null,
                        OverUnder: overUnders.Count > 0 ? (decimal?)overUnders.Average() : null);
                });

            var graded = new List<(int Week, decimal ProdSpread, decimal ExpSpread,
                                    decimal ProdTotal, decimal ExpTotal,
                                    decimal? VegasSpread, decimal? VegasSpreadOpen, decimal? VegasTotal,
                                    decimal ActualMargin, decimal ActualTotal, bool NeutralSite)>();

            foreach (var r in rows)
            {
                if (!resultsByGameId.TryGetValue(r.GameId, out var actual)) continue;
                if (r.ProductionSpread is null || r.ExperimentalSpread is null ||
                    r.ProductionTotal  is null || r.ExperimentalTotal  is null) continue;

                lineByGame.TryGetValue(r.GameId, out var line);

                graded.Add((r.Week, r.ProductionSpread.Value, r.ExperimentalSpread.Value,
                            r.ProductionTotal.Value, r.ExperimentalTotal.Value,
                            line.Spread, line.SpreadOpen, line.OverUnder,
                            actual.Margin, actual.Total, actual.NeutralSite));
            }

            AccuracyStats Grade(IEnumerable<(decimal Spread, decimal Total, decimal ActualMargin, decimal ActualTotal)> items)
            {
                var list = items.ToList();
                if (list.Count == 0) return new AccuracyStats(0, 0m, 0m, 0m, 0m, 0m);

                var mae        = list.Average(x => Math.Abs(x.Spread - x.ActualMargin));
                var totalMae   = list.Average(x => Math.Abs(x.Total  - x.ActualTotal));
                var spreadBias = list.Average(x => x.Spread - x.ActualMargin);
                var totalBias  = list.Average(x => x.Total  - x.ActualTotal);

                // "Winner correct" = predicted margin's sign matches actual margin's
                // sign. A genuine tie (ActualMargin == 0, essentially impossible in
                // modern CFB) always counts as a miss, since no predicted sign can
                // match "no winner."
                var correct = list.Count(x =>
                    x.ActualMargin != 0 && Math.Sign(x.Spread) == Math.Sign(x.ActualMargin));
                var winnerAccuracyPct = Math.Round(100m * correct / list.Count, 1);

                return new AccuracyStats(
                    Games: list.Count,
                    Mae: Math.Round(mae, 2),
                    WinnerAccuracyPct: winnerAccuracyPct,
                    SpreadBias: Math.Round(spreadBias, 2),
                    TotalMae: Math.Round(totalMae, 2),
                    TotalBias: Math.Round(totalBias, 2));
            }

            var productionOverall = Grade(graded.Select(x =>
                (x.ProdSpread, x.ProdTotal, x.ActualMargin, x.ActualTotal)));
            var experimentalOverall = Grade(graded.Select(x =>
                (x.ExpSpread, x.ExpTotal, x.ActualMargin, x.ActualTotal)));

            // Closing and opening spread graded against actual margin; total (O/U)
            // graded separately since a game can have a spread line without a total
            // line or vice versa — don't want a missing total to exclude a game
            // that does have a usable spread, and vice versa.
            var vegasClosingOverall = GradeVegasSpreadAndTotal(graded, useOpen: false);
            var vegasOpeningOverall = GradeVegasSpreadAndTotal(graded, useOpen: true);

            var byWeek = graded
                .GroupBy(x => x.Week)
                .OrderBy(g => g.Key)
                .Select(g => new WeeklyAccuracyComparison(
                    Week: g.Key,
                    Production: Grade(g.Select(x => (x.ProdSpread, x.ProdTotal, x.ActualMargin, x.ActualTotal))),
                    Experimental: Grade(g.Select(x => (x.ExpSpread, x.ExpTotal, x.ActualMargin, x.ActualTotal))),
                    VegasClosing: GradeVegasSpreadAndTotal(g.ToList(), useOpen: false),
                    VegasOpening: GradeVegasSpreadAndTotal(g.ToList(), useOpen: true)))
                .ToList();

            // Home vs Neutral split — neutral-site games get zero HFA applied by
            // CalculatePrediction (RatingCalculator.ApplyHomeField), so if spreadBias
            // is concentrated in Home and mostly absent in Neutral, that's specific
            // evidence pointing at the HFA constant rather than a general miscalibration.
            var byLocation = graded
                .GroupBy(x => x.NeutralSite)
                .OrderBy(g => g.Key) // false (Home) first, then true (Neutral)
                .Select(g => new LocationAccuracyComparison(
                    Location: g.Key ? "Neutral" : "Home",
                    Production: Grade(g.Select(x => (x.ProdSpread, x.ProdTotal, x.ActualMargin, x.ActualTotal))),
                    Experimental: Grade(g.Select(x => (x.ExpSpread, x.ExpTotal, x.ActualMargin, x.ActualTotal))),
                    VegasClosing: GradeVegasSpreadAndTotal(g.ToList(), useOpen: false),
                    VegasOpening: GradeVegasSpreadAndTotal(g.ToList(), useOpen: true)))
                .ToList();

            return new RatingMethodAccuracyComparison(
                Year: year,
                TotalGames: graded.Count,
                Production: productionOverall,
                Experimental: experimentalOverall,
                VegasClosing: vegasClosingOverall,
                VegasOpening: vegasOpeningOverall,
                ByWeek: byWeek,
                ByLocation: byLocation);
        }

        /// <summary>
        /// Grades Vegas spread (closing or opening, per useOpen) and total against
        /// actual results. Spread and total are graded on separately-filtered
        /// subsets — a game missing one line type shouldn't be excluded from the
        /// other's stats. MAE/bias/totalMae/totalBias are computed independently
        /// and combined into one AccuracyStats for reporting convenience; this
        /// means "Games" reflects the spread-line count specifically (the more
        /// commonly available line type), not a strict intersection of both.
        /// </summary>
        private static AccuracyStats? GradeVegasSpreadAndTotal(
            List<(int Week, decimal ProdSpread, decimal ExpSpread, decimal ProdTotal, decimal ExpTotal,
                  decimal? VegasSpread, decimal? VegasSpreadOpen, decimal? VegasTotal,
                  decimal ActualMargin, decimal ActualTotal, bool NeutralSite)> items,
            bool useOpen)
        {
            var spreadItems = items
                .Select(x => (Spread: useOpen ? x.VegasSpreadOpen : x.VegasSpread, x.ActualMargin))
                .Where(x => x.Spread.HasValue)
                .ToList();

            if (spreadItems.Count == 0) return null;

            var mae        = spreadItems.Average(x => Math.Abs(x.Spread!.Value - x.ActualMargin));
            var spreadBias = spreadItems.Average(x => x.Spread!.Value - x.ActualMargin);
            var correct    = spreadItems.Count(x =>
                x.ActualMargin != 0 && Math.Sign(x.Spread!.Value) == Math.Sign(x.ActualMargin));
            var winnerAccuracyPct = Math.Round(100m * correct / spreadItems.Count, 1);

            var totalItems = items
                .Where(x => x.VegasTotal.HasValue)
                .Select(x => (Total: x.VegasTotal!.Value, x.ActualTotal))
                .ToList();

            decimal totalMae  = totalItems.Count > 0 ? totalItems.Average(x => Math.Abs(x.Total - x.ActualTotal)) : 0m;
            decimal totalBias = totalItems.Count > 0 ? totalItems.Average(x => x.Total - x.ActualTotal) : 0m;

            return new AccuracyStats(
                Games: spreadItems.Count,
                Mae: Math.Round(mae, 2),
                WinnerAccuracyPct: winnerAccuracyPct,
                SpreadBias: Math.Round(spreadBias, 2),
                TotalMae: Math.Round(totalMae, 2),
                TotalBias: Math.Round(totalBias, 2));
        }
    }
}
