using Microsoft.Extensions.Options;
using SaturdayPulse.Api.Contracts.Responses;
using SaturdayPulse.Configuration;
using SaturdayPulse.Contracts;
using SaturdayPulse.Contracts.Requests;
using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.Interfaces;
using SaturdayPulse.Models;
using SaturdayPulse.Utilities;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Encapsulates all data-access and business logic for development/admin operations.
    /// Pass 2 complete: all EF queries moved to repositories.
    /// No direct _context references remain.
    /// </summary>
    public class DeveloperService
    {
        private readonly IUnitOfWork               _uow;
        private readonly IGameDataService          _gameDataService;
        private readonly TeamMetricsService        _teamMetrics;
        private readonly RollingAverageService     _rollingAverageService;
        private readonly ScoreDeltaCalculator      _scoreDeltaCalculator;
        private readonly MatchupHistoryCalculator  _matchupHistoryCalculator;
        private readonly WeeklyRankingsService     _weeklyRankingsService;
        private readonly GamePredictionService     _predictionService;
        private readonly MetricsConfiguration      _config;
        private readonly ILogger<DeveloperService> _logger;

        public DeveloperService(
            IUnitOfWork uow,
            IGameDataService gameDataService,
            TeamMetricsService teamMetrics,
            RollingAverageService rollingAverageService,
            ScoreDeltaCalculator scoreDeltaCalculator,
            MatchupHistoryCalculator matchupHistoryCalculator,
            WeeklyRankingsService weeklyRankingsService,
            GamePredictionService predictionService,
            IOptions<MetricsConfiguration> config,
            ILogger<DeveloperService> logger)
        {
            _uow                      = uow;
            _gameDataService          = gameDataService;
            _teamMetrics              = teamMetrics;
            _rollingAverageService    = rollingAverageService;
            _scoreDeltaCalculator     = scoreDeltaCalculator;
            _matchupHistoryCalculator = matchupHistoryCalculator;
            _weeklyRankingsService    = weeklyRankingsService;
            _predictionService        = predictionService;
            _config                   = config.Value;
            _logger                   = logger;
        }

        public Task<int> LoadConferencesAsync(CancellationToken token = default)
            => _gameDataService.LoadConferencesAsync(token);

        public Task<int> LoadTeamsAsync(int? year, CancellationToken token = default)
            => _gameDataService.LoadTeamsAsync(year, token);

        public Task<int> LoadTeamsBulkAsync(int startYear, CancellationToken token = default)
            => _gameDataService.LoadTeamsBulkAsync(startYear, token);

        public Task<int> AssignPostseasonWeeksAsync(int year, CancellationToken token = default)
            => _gameDataService.AssignPostseasonWeeksAsync(year, token);

        public Task<int> AssignPostseasonWeeksBulkAsync(int startYear, CancellationToken token = default)
            => _gameDataService.AssignPostseasonWeeksBulkAsync(startYear, token);

        public Task<int> LoadGamesAsync(int year, int? week, CancellationToken token = default)
            => _gameDataService.LoadGamesAsync(year, week, token);

        public Task<int> LoadGamesBulkAsync(int startYear, CancellationToken token = default)
            => _gameDataService.LoadGamesBulkAsync(startYear, token);

        public Task<int> LoadLinesAsync(int year, int week, CancellationToken token = default)
            => _gameDataService.LoadLinesAsync(year, week, token);

        public Task<int> LoadLinesBulkAsync(int startYear, CancellationToken token = default)
            => _gameDataService.LoadLinesBulkAsync(startYear, token);
        public Task<int> BuildTeamsConferenceHistoryAsync(int startYear, CancellationToken token = default)
    => _gameDataService.BuildTeamsConferenceHistoryAsync(startYear, token);

        public Task<int> WeeklyRefreshAsync(int year, int week, CancellationToken token = default)
            => _gameDataService.WeeklyRefreshAsync(year, week, token);

        // ── Rolling Averages ──────────────────────────────────────────────────────

        public async Task<BackfillResult> BackfillRollingAveragesAsync(int? startYear, CancellationToken token)
        {
            var allRecords = await _uow.TeamRecords.GetSinceYearWithTeamsAsync(1960, token);
            var years      = allRecords.Select(tr => (int)tr.Year).Distinct().OrderBy(y => y).ToList();

            if (startYear.HasValue)
                years = years.Where(y => y >= startYear.Value).ToList();

            if (!years.Any())
                throw new InvalidOperationException("No TeamRecords found matching the criteria.");

            _logger.LogInformation("Backfilling rolling averages for {Count} years...", years.Count);

            int processed = 0;
            foreach (var year in years)
            {
                await _rollingAverageService.ComputeAndPersistAsync(year, week: null, token);
                processed++;
                _logger.LogInformation("Rolling averages complete: {Year} ({Done}/{Total})", year, processed, years.Count);
            }

            return new BackfillResult("Backfill complete.", processed, startYear);
        }

        public async Task<object> CalculateRollingAveragesAsync(int? year, int? week, CancellationToken token)
        {
            var targetYear = year ?? DateTime.Now.Year;
            await _rollingAverageService.ComputeAndPersistAsync(targetYear, week, token);
            return new
            {
                message        = $"Rolling averages computed for {targetYear}" +
                                 (week.HasValue ? $" week {week.Value}" : " (preseason)"),
                year           = targetYear, week, liveSwapActive = week.HasValue
            };
        }

        // ── Team Records and Metrics ──────────────────────────────────────────────

        public Task UpdateTeamRecordsAsync(int? year)
            => _gameDataService.UpdateTeamRecordsAsync(year);

        public Task SetSOSAsync(int? year, int? week)
            => _teamMetrics.SetSOS(year, week);

        public Task CalculatePowerRatingsAsync(int? year)
            => _teamMetrics.CalculatePowerRatings(year);

        public Task CalculateRankingsAsync(int targetYear)
            => _teamMetrics.CalculateRankings(targetYear);

        public async Task RecalculateMetricsAsync(int year, int? week)
        {
            await _rollingAverageService.ComputeAndPersistAsync(year, week);
            await _teamMetrics.SetSOS(year, week);
            await _teamMetrics.CalculatePowerRatings(year);
            await _teamMetrics.CalculateRankings(year);
        }

        public async Task<BackfillResult> BackfillAllMetricsAsync(int? startYear, CancellationToken token)
        {
            var allRecords = await _uow.TeamRecords.GetSinceYearWithTeamsAsync(1960, token);
            var years      = allRecords.Select(tr => tr.Year).Distinct().OrderBy(y => y).ToList();

            if (startYear.HasValue)
                years = years.Where(y => y >= startYear.Value).ToList();

            foreach (var year in years)
            {
                _logger.LogInformation("Processing year {Year}", year);
                await RecalculateMetricsAsync((int)year, null);
            }

            return new BackfillResult("Backfill completed successfully.", years.Count,
                years.Any() ? (int?)years.First() : null);
        }

        // ── Score Deltas and Rivalries ────────────────────────────────────────────

        public async Task<RecalculateScoreDeltasResult> RecalculateScoreDeltasAsync(CancellationToken token)
        {
            await _scoreDeltaCalculator.UpdateAvgScoreDeltasTableAsync();
            var deltas = await _uow.Lookups.GetAvgScoreDeltasAsync(token);
            return new RecalculateScoreDeltasResult(
                "Score deltas recalculated successfully", deltas.Count,
                "5% win percentage increments",
                "Predictions will now use updated delta statistics");
        }

        public async Task<RecreateTableResult> RecreateAvgScoreDeltasTableAsync(CancellationToken token)
        {
            await _uow.Lookups.ClearAvgScoreDeltasAsync(token);
            _logger.LogInformation("AvgScoreDeltas table cleared");

            await _scoreDeltaCalculator.UpdateAvgScoreDeltasTableAsync();

            var deltas = await _uow.Lookups.GetAvgScoreDeltasAsync(token);
            return new RecreateTableResult("AvgScoreDeltas table recreated successfully", deltas.Count, "Table cleared and repopulated");
        }

        public async Task<MatchupHistoriesResult> CalculateMatchupHistoriesAsync()
        {
            var count = await _matchupHistoryCalculator.CalculateAllMatchupHistories();
            return new MatchupHistoriesResult(
                "Matchup histories calculated successfully", count, 50,
                "Matchup-specific variance will now be used in predictions");
        }

        // ── Analytics and Diagnostics ─────────────────────────────────────────────

        public async Task<AnalyticsResult> GetAnalyticsAsync(int? startYear, int? endYear, CancellationToken token)
        {
            var records = await _uow.TeamRecords.QueryAsync(
                startYear: startYear, endYear: endYear, limit: int.MaxValue, token: token);

            records = records.Where(tr => tr.PowerRating != null).ToList();

            var overperformers = records
                .Where(tr => tr.Wins > (tr.CombinedSOS ?? 0) * 12)
                .OrderByDescending(tr => tr.Wins - (tr.CombinedSOS ?? 0) * 12)
                .Take(10)
                .Select(tr => (object)new
                {
                    tr.Year, TeamName = tr.Teams?.TeamName, Record = $"{tr.Wins}-{tr.Losses}",
                    tr.CombinedSOS, tr.PowerRating,
                    Overperformance = tr.Wins - (tr.CombinedSOS ?? 0) * 12
                });

            var underperformers = records
                .Where(tr => tr.Wins < (tr.CombinedSOS ?? 0) * 12)
                .OrderBy(tr => tr.Wins - (tr.CombinedSOS ?? 0) * 12)
                .Take(10)
                .Select(tr => (object)new
                {
                    tr.Year, TeamName = tr.Teams?.TeamName, Record = $"{tr.Wins}-{tr.Losses}",
                    tr.CombinedSOS, tr.PowerRating,
                    Underperformance = (tr.CombinedSOS ?? 0) * 12 - tr.Wins
                });

            return new AnalyticsResult(
                records.Count,
                $"{startYear ?? records.Min(r => r.Year)}-{endYear ?? records.Max(r => r.Year)}",
                overperformers, underperformers,
                records.Average(r => (double?)r.PowerRating),
                records.Average(r => (double?)r.CombinedSOS));
        }

        public async Task<TeamGameAnalysisResult> AnalyzeTeamGamesAsync(int teamId, int? year, CancellationToken token)
        {
            var targetYear     = year ?? DateTime.Now.Year;
            var allGames       = await _uow.Games.GetByYearAsync(targetYear, token);
            var teamGames      = allGames
                .Where(g => g.HomeId == teamId || g.AwayId== teamId)
                .OrderBy(g => g.Week).ToList();

            var teamRecords    = await _uow.TeamRecords.GetByYearAsync(targetYear, token);
            var winsLookup     = teamRecords.ToDictionary(tr => tr.TeamID, tr => (int)tr.Wins);
            var lossesLookup   = teamRecords.ToDictionary(tr => tr.TeamID, tr => (int)tr.Losses);
            var avgScoreDeltas = await _uow.Lookups.GetAvgScoreDeltasAsync(token);
            var hfa            = _config.HomeFieldAdvantage;

            var analysis = teamGames.Select(g =>
            {
                bool isWinner       = g.HomeId == teamId && g.HomePoints > g.AwayPoints;
                var teamPoints      = g.HomeId == teamId ? g.HomePoints : g.AwayPoints;
                var oppPoints       = g.HomeId == teamId ? g.AwayPoints : g.HomePoints;
                var oppId           = g.HomeId == teamId ? g.AwayId : g.HomeId;
                var delta           = teamPoints - oppPoints;
                bool isHomeTeam     = g.HomeId == teamId;
                var locationDisplay = isHomeTeam ? "Home" : g.NeutralSite ? "Neutral" : "Away";
                var result          = isWinner ? "W" : "L";
                var opponentName    = isWinner ? g.AwayName : g.HomeName;

                var teamWins   = winsLookup.GetValueOrDefault(teamId, 0);
                var teamLosses = lossesLookup.GetValueOrDefault(teamId, 0);
                var oppWins    = winsLookup.GetValueOrDefault((int)oppId,   0);
                var oppLosses  = lossesLookup.GetValueOrDefault((int)oppId, 0);

                var teamWinPct = RatingCalculator.BucketWinPct(teamWins, teamWins + teamLosses);
                var oppWinPct  = RatingCalculator.BucketWinPct(oppWins,  oppWins  + oppLosses);
                var maxWinPct  = Math.Max(teamWinPct, oppWinPct);
                var minWinPct  = Math.Min(teamWinPct, oppWinPct);

                var asd = avgScoreDeltas.FirstOrDefault(a => a.Team1WinPct == maxWinPct && a.Team2WinPct == minWinPct);

                double zScore = 0.0, expectedDelta = 0.0, homeAdjustment = 0.0;

                if (asd != null && asd.StDevP != 0)
                {
                    expectedDelta = (double)asd.AverageScoreDelta;
                    var expectedFromTeam = RatingCalculator.ExpectedFromPerspective(expectedDelta, teamWinPct, oppWinPct);

                    if (isHomeTeam)             { expectedFromTeam += hfa; homeAdjustment =  hfa; }
                    else if (g.NeutralSite) { expectedFromTeam -= hfa; homeAdjustment = -hfa; }

                    zScore = (double)((delta - expectedFromTeam) / (double)asd.StDevP);
                }

                var baseExpected     = teamWins >= oppWins ? expectedDelta : -expectedDelta;
                var adjustedExpected = baseExpected + homeAdjustment;

                return (object)new
                {
                    g.Week, OpponentName = opponentName, Location = locationDisplay, result, delta,
                    TeamFinalWins         = teamWins, OppFinalWins = oppWins,
                    BaseExpectedDelta     = Math.Round(baseExpected,     1),
                    HomeAdjustment        = Math.Round(homeAdjustment,   1),
                    AdjustedExpectedDelta = Math.Round(adjustedExpected, 1),
                    ActualDelta           = delta,
                    Difference            = Math.Round((double)delta - adjustedExpected, 1),
                    ZScore                = Math.Round(zScore, 3),
                    Performance           = zScore > _config.DominantPerformanceThreshold ? "Dominant"
                                          : zScore > _config.UnderperformedThreshold ? "Expected"
                                          : "Underperformed"
                };
            }).ToList();

            var avgZScore  = analysis.Average(a => ((dynamic)a).ZScore);
            var teamRecord = await _uow.TeamRecords.GetByTeamAndYearAsync(teamId, targetYear, token);

            return new TeamGameAnalysisResult(
                teamId, targetYear, $"{teamRecord?.Wins}-{teamRecord?.Losses}",
                teamRecord?.CombinedSOS, Math.Round(avgZScore, 4), teamRecord?.PowerRating,
                Math.Round(avgZScore * (double)(teamRecord?.CombinedSOS ?? 1.0m), 4),
                analysis);
        }

        public async Task<TrendsResult> CalculateTrendsAsync(int? teamId, int? year, CancellationToken token)
        {
            var targetYear = year ?? DateTime.Now.Year;
            var records    = await _uow.TeamRecords.GetByYearWithTeamsAsync(targetYear, token);
            records        = records.Where(tr => tr.PowerRating != null).ToList();

            if (teamId.HasValue)
                records = records.Where(tr => tr.TeamID == teamId.Value).ToList();

            var trends = records.Select(tr => (object)new
            {
                TeamId                = tr.TeamID, TeamName = tr.Teams?.TeamName,
                tr.Year, Record       = $"{tr.Wins}-{tr.Losses}",
                tr.PowerRating, tr.CombinedSOS, tr.Ranking,
                WinPercentage         = (decimal)tr.Wins / (tr.Wins + tr.Losses),
                ProjectedFinalRanking = tr.Ranking,
                Trend                 = tr.PowerRating > 0.02m ? "Ascending"
                                      : tr.PowerRating < -0.02m ? "Descending"
                                      : "Stable"
            }).ToList();

            return new TrendsResult(targetYear, trends.Count, trends);
        }

        // ── Weekly Rankings ───────────────────────────────────────────────────────

        public async Task<WeeklyRankingsBackfillResult> BackfillWeeklyRankingsAsync(int? startYear, CancellationToken token)
        {
            var fromYear = startYear ?? 1960;
            var allGames = await _uow.Games.GetPlayedGamesSinceYearAsync(fromYear, token);

            var yearWeeks = allGames
                .Select(g => new { g.Year, g.Week })
                .Distinct()
                .OrderBy(g => g.Year).ThenBy(g => g.Week)
                .ToList();

            if (!yearWeeks.Any())
                throw new InvalidOperationException("No played games found matching the criteria.");

            _logger.LogInformation("Backfilling WeeklyRankings for {Count} year/week combinations...", yearWeeks.Count);

            int processed = 0;
            foreach (var yw in yearWeeks)
            {
                await _weeklyRankingsService.ComputeAndSaveAsync(yw.Year, yw.Week, token);
                processed++;
                _logger.LogInformation("Completed {Year} week {Week} ({Done}/{Total})", yw.Year, yw.Week, processed, yearWeeks.Count);
            }

            return new WeeklyRankingsBackfillResult("Backfill complete.", processed, startYear);
        }

        public async Task<ComputeWeeklyResult> ComputeWeeklyAsync(int? year, int? week, bool backfill, CancellationToken token)
        {
            var targetYear = year ?? DateTime.Now.Year;

            if (backfill)
            {
                await _weeklyRankingsService.BackfillYearAsync(targetYear, token);
                return new ComputeWeeklyResult($"Backfilled all weeks for {targetYear}.", targetYear, null);
            }

            if (!week.HasValue)
                throw new ArgumentException("Provide week=N or backfill=true.");

            await _weeklyRankingsService.ComputeAndSaveAsync(targetYear, week.Value, token);
            return new ComputeWeeklyResult($"Computed weekly rankings for {targetYear} week {week.Value}.", targetYear, week.Value);
        }

        /// <summary>
        /// Backfills the Projections table for every year/week in the database.
        ///
        /// Strategy mirrors BackfillWeeklyRankingsAsync:
        ///   • Discover all distinct (Year, Week) snapshots from WeeklyRankings
        ///     (guarantees ratings exist for each slot we try to project from).
        ///   • For each snapshot, fetch unplayed games in that season from that
        ///     week onward, run predictions using that snapshot's power ratings,
        ///     and upsert into Projections.
        ///
        /// "Unplayed at week W" = regular-season games with Week > snapshotWeek
        /// that have no recorded score yet (or equivalently, all future-week games
        /// relative to the snapshot — historical backfill treats every game after
        /// the snapshot week as "remaining").
        /// </summary>
        public async Task<BackfillResult> BackfillProjectionsAsync(
            int? startYear, CancellationToken token = default)
        {
            const int firstYear = 1965;
            var effectiveStart = startYear ?? firstYear;

            // All distinct (Year, Week) pairs that have WeeklyRankings data,
            // filtered by startYear, ordered chronologically.
            var snapshots = await _uow.WeeklyRankings
                .GetDistinctYearWeeksAsync(token);               // returns List<(int Year, int Week)>

            snapshots = snapshots
                .Where(s => s.Year >= effectiveStart)
                .OrderBy(s => s.Year).ThenBy(s => s.Week)
                .ToList();

            if (snapshots.Count == 0)
                throw new InvalidOperationException(
                    $"No WeeklyRankings data found from {effectiveStart} onward.");

            // Cache data that doesn't change across the loop.
            var teamsById   = await _uow.Teams.GetDictionaryByTeamIdAsync(token);
            var teamsByName = teamsById.Values.ToDictionary(t => t.TeamName);
            var avgScoreDeltas = await _uow.Lookups.GetAvgScoreDeltasAsync(token);
            var rivalries = await _uow.Lookups.GetMatchupHistoriesAsync(token);

            int processed = 0;

            foreach (var (snapshotYear, snapshotWeek) in snapshots)
            {
                token.ThrowIfCancellationRequested();

                // All regular-season games for this year.
                var allGames = await _uow.Games.GetByYearAsync(snapshotYear, token);

                // "Remaining" from this snapshot's perspective = weeks after the snapshot.
                // For a historical backfill every game in week > snapshotWeek is "unplayed".
                var maxWeek = allGames.Max(g => g.Week); 
                var remainingGames = allGames
                    .Where(g => g.Week > snapshotWeek && g.Week <= maxWeek)
                    .ToList();

                if (remainingGames.Count == 0) continue;

                var weeklyRankings = await _uow.WeeklyRankings
                    .GetByYearAndWeekAsync(snapshotYear, snapshotWeek, token);

                var powerByTeamId = weeklyRankings
                    .Where(wr => wr.PowerRating.HasValue)
                    .ToDictionary(wr => wr.TeamID, wr => wr.PowerRating!.Value);

                var matchupRequests = new List<MatchupRequest>(remainingGames.Count);

                foreach (var g in remainingGames)
                {
                    var homeId = g.HomeId ?? 0;
                    var awayId = g.AwayId ?? 0;
                    if (!teamsById.TryGetValue(homeId, out var homeTeam)) continue;
                    if (!teamsById.TryGetValue(awayId, out var awayTeam)) continue;

                    char location = g.NeutralSite == true ? 'N' : 'W';

                    matchupRequests.Add(new MatchupRequest
                    {
                        TeamName     = homeTeam.TeamName,
                        OpponentName = awayTeam.TeamName,
                        Location     = location,
                        Week         = g.Week
                    });
                }

                if (matchupRequests.Count == 0) continue;

                // PredictMatchups uses current TeamRecords for W/L and PPG,
                // but power ratings in TeamRecord.PowerRating reflect the
                // end-of-season values. For historical fidelity we override
                // power ratings from the weekly snapshot after prediction.
                var predictions = await _predictionService.PredictMatchups(
                    snapshotYear, matchupRequests, token);

                // Override power ratings with snapshot values so spread
                // reflects what the model knew at snapshotWeek, not season-end.
                for (int i = 0; i < predictions.Count; i++)
                {
                    var p = predictions[i];
                    if (teamsByName.TryGetValue(p.TeamName, out var t) &&
                        teamsByName.TryGetValue(p.OpponentName, out var opp))
                    {
                        var teamPR = powerByTeamId.GetValueOrDefault(t.TeamId, 0m);
                        var oppPR  = powerByTeamId.GetValueOrDefault(opp.TeamId, 0m);

                        // Re-apply the power rating delta on top of the existing margin.
                        // This matches the adjustment in GamePredictionService.CalculatePrediction.
                        var delta = (double)(teamPR - oppPR) * 10.0;
                        p.ExpectedMargin = Math.Round(p.ExpectedMargin + delta, 1);
                    }
                }

                var projections = new List<Projection>(remainingGames.Count);

                foreach (var g in remainingGames)
                {
                    var homeId = g.HomeId ?? 0;
                    var awayId = g.AwayId ?? 0;
                    if (!teamsById.TryGetValue(homeId, out var homeTeam)) continue;
                    if (!teamsById.TryGetValue(awayId, out var awayTeam)) continue;

                    var pred = predictions.FirstOrDefault(p =>
                        p.TeamName     == homeTeam.TeamName &&
                        p.OpponentName == awayTeam.TeamName &&
                        p.Week         == g.Week);

                    if (pred == null) continue;

                    projections.Add(GamePredictionService.BuildProjection(
                        prediction: pred,
                        gameId:     g.GameId,
                        year:       snapshotYear,
                        week:       snapshotWeek,
                        homeTeamId: homeId,
                        awayTeamId: awayId));
                }

                await _uow.Projections.UpsertManyAsync(projections, token);
                processed++;

                _logger.LogInformation(
                    "Projections backfill: {Year} week {Week} — {Count} games projected",
                    snapshotYear, snapshotWeek, projections.Count);
            }

            return new BackfillResult(
                $"Projections backfill complete — {processed} snapshots processed.",
                processed,
                effectiveStart);
        }

    }
}
