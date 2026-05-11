using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NCAA_Power_Ratings.Configuration;
using NCAA_Power_Ratings.Contracts;
using NCAA_Power_Ratings.Data;
using NCAA_Power_Ratings.Interfaces;
using NCAA_Power_Ratings.Models;
using NCAA_Power_Ratings.Utilities;
using NCAA_Power_Ratings.Contracts.Responses;

namespace NCAA_Power_Ratings.Services
{
    /// <summary>
    /// Encapsulates all data-access and business logic for development/admin operations.
    /// Uses IUnitOfWork for all data access.
    /// </summary>
    public class DeveloperService
    {
        private readonly IUnitOfWork              _uow;
        private readonly IGameDataService         _gameDataService;
        private readonly TeamMetricsService       _teamMetrics;
        private readonly RollingAverageService    _rollingAverageService;
        private readonly ScoreDeltaCalculator     _scoreDeltaCalculator;
        private readonly MatchupHistoryCalculator _matchupHistoryCalculator;
        private readonly WeeklyRankingsService    _weeklyRankingsService;
        private readonly MetricsConfiguration     _config;
        private readonly ILogger<DeveloperService> _logger;

        // Direct context access retained for Pass 2
        private readonly NCAAContext _context;

        public DeveloperService(
            IUnitOfWork uow,
            IGameDataService gameDataService,
            TeamMetricsService teamMetrics,
            RollingAverageService rollingAverageService,
            ScoreDeltaCalculator scoreDeltaCalculator,
            MatchupHistoryCalculator matchupHistoryCalculator,
            WeeklyRankingsService weeklyRankingsService,
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
            _config                   = config.Value;
            _logger                   = logger;
            _context                  = ((Infrastructure.UnitOfWork)uow).Context;
        }

        // ── Data loading ──────────────────────────────────────────────────────────

        public Task<List<Game>> ExtractGameDataHistoryAsync(int? startYear)
            => _gameDataService.ExtractGameDataHistoryAsync(startYear);

        public async Task<int> LoadGameHistoryFromFilesAsync()
            => await _gameDataService.LoadGameHistoryFromFiles();

        public Task<int> ProcessSingleFileAsync(string filePath, CancellationToken token)
            => _gameDataService.ProcessSingleFileAsync(filePath, token);

        public async Task<object> UpdateWeekGamesFromFileAsync(int year, int week, CancellationToken token)
        {
            var fileName = $"{year}.txt";
            var dataDir  = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "NCAA Raw Game Data");
            var filePath = System.IO.Path.Combine(dataDir, fileName);

            if (!System.IO.File.Exists(filePath))
                throw new System.IO.FileNotFoundException($"File '{fileName}' not found in NCAA Raw Game Data directory.");

            var gamesProcessed = await _gameDataService.UpdateGameDataFromFileAsync(filePath, year, week, token);
            await _gameDataService.UpdateTeamRecordsAsync(year);
            await RecalculateMetricsAsync(year, week);

            return new
            {
                message           = $"Successfully processed games for {year} week {week} from file",
                gamesProcessed,
                metricsCalculated = "TeamRecords, SOS, PowerRating, and Ranking recalculated",
                year, week, sourceFile = fileName
            };
        }

        public async Task<object> UpdateWeekGamesAsync(int year, int week)
        {
            var gamesProcessed = await _gameDataService.UpdateGameDataForYearAndWeekAsync(year, week);
            await _gameDataService.UpdateTeamRecordsAsync(year);
            await RecalculateMetricsAsync(year, week);

            return new { message = $"Successfully updated games for {year} week {week}", gamesProcessed, year, week };
        }

        public AvailableFilesResult ListAvailableFiles()
        {
            var dataDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "NCAA Raw Game Data");

            if (!System.IO.Directory.Exists(dataDir))
                throw new System.IO.DirectoryNotFoundException("NCAA Raw Game Data directory not found.");

            var files = System.IO.Directory.GetFiles(dataDir, "*.txt")
                .Select(f => (object)new
                {
                    fileName     = System.IO.Path.GetFileName(f),
                    fullPath     = f,
                    size         = new System.IO.FileInfo(f).Length,
                    lastModified = System.IO.File.GetLastWriteTime(f)
                })
                .OrderBy(f => ((dynamic)f).fileName)
                .ToList();

            return new AvailableFilesResult(dataDir, files.Count, files);
        }

        // ── Rolling averages ──────────────────────────────────────────────────────

        public async Task<BackfillResult> BackfillRollingAveragesAsync(int? startYear, CancellationToken token)
        {
            var yearsQuery = _context.TeamRecords.Select(tr => (int)tr.Year).Distinct();
            if (startYear.HasValue)
                yearsQuery = yearsQuery.Where(y => y >= startYear.Value);

            var years = await yearsQuery.OrderBy(y => y).ToListAsync(token);
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
                year           = targetYear,
                week,
                liveSwapActive = week.HasValue
            };
        }

        // ── Team records and metrics ──────────────────────────────────────────────

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
            var years = await _context.TeamRecords
                .Select(tr => tr.Year)
                .Distinct()
                .OrderBy(y => y)
                .ToListAsync(token);

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

        // ── Score deltas and rivalries ────────────────────────────────────────────

        public async Task<RecalculateScoreDeltasResult> RecalculateScoreDeltasAsync(CancellationToken token)
        {
            await _scoreDeltaCalculator.UpdateAvgScoreDeltasTableAsync();
            var count = await _context.AvgScoreDeltas.CountAsync(token);

            return new RecalculateScoreDeltasResult(
                "Score deltas recalculated successfully", count,
                "5% win percentage increments",
                "Predictions will now use updated delta statistics");
        }

        public async Task<RecreateTableResult> RecreateAvgScoreDeltasTableAsync(CancellationToken token)
        {
            await _context.Database.ExecuteSqlRawAsync("DELETE FROM AvgScoreDeltas", token);
            _logger.LogInformation("AvgScoreDeltas table cleared");

            await _scoreDeltaCalculator.UpdateAvgScoreDeltasTableAsync();

            var count = await _context.AvgScoreDeltas.CountAsync(token);
            return new RecreateTableResult("AvgScoreDeltas table recreated successfully", count, "Table cleared and repopulated");
        }

        public async Task<MatchupHistoriesResult> CalculateMatchupHistoriesAsync()
        {
            var count = await _matchupHistoryCalculator.CalculateAllMatchupHistories();
            return new MatchupHistoriesResult(
                "Matchup histories calculated successfully", count, 50,
                "Matchup-specific variance will now be used in predictions");
        }

        // ── Analytics and diagnostics ─────────────────────────────────────────────

        public async Task<AnalyticsResult> GetAnalyticsAsync(int? startYear, int? endYear, CancellationToken token)
        {
            var query = _context.TeamRecords
                .Include(tr => tr.Team)
                .Where(tr => tr.PowerRating != null);

            if (startYear.HasValue) query = query.Where(tr => tr.Year >= startYear.Value);
            if (endYear.HasValue)   query = query.Where(tr => tr.Year <= endYear.Value);

            var records = await query.ToListAsync(token);

            var overperformers = records
                .Where(tr => tr.Wins > (tr.CombinedSOS ?? 0) * 12)
                .OrderByDescending(tr => tr.Wins - (tr.CombinedSOS ?? 0) * 12)
                .Take(10)
                .Select(tr => (object)new
                {
                    tr.Year, TeamName = tr.Team?.TeamName,
                    Record = $"{tr.Wins}-{tr.Losses}",
                    tr.CombinedSOS, tr.PowerRating,
                    Overperformance = tr.Wins - (tr.CombinedSOS ?? 0) * 12
                });

            var underperformers = records
                .Where(tr => tr.Wins < (tr.CombinedSOS ?? 0) * 12)
                .OrderBy(tr => tr.Wins - (tr.CombinedSOS ?? 0) * 12)
                .Take(10)
                .Select(tr => (object)new
                {
                    tr.Year, TeamName = tr.Team?.TeamName,
                    Record = $"{tr.Wins}-{tr.Losses}",
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
            var targetYear = year ?? DateTime.Now.Year;

            var gamesFromWinner = _context.Game
                .Where(g => g.Year == targetYear && g.WinnerId == teamId)
                .Select(g => new
                {
                    g.Week, TeamId = g.WinnerId, TeamName = g.WinnerName,
                    OpponentId = g.LoserId, OpponentName = g.LoserName,
                    TeamPoints = g.WPoints, OpponentPoints = g.LPoints,
                    Delta = g.WPoints - g.LPoints, Result = "W", g.Location,
                    IsHomeTeam = g.Location == 'W',
                    LocationDisplay = g.Location == 'W' ? "Home" : g.Location == 'L' ? "Away" : "Neutral"
                });

            var gamesFromLoser = _context.Game
                .Where(g => g.Year == targetYear && g.LoserId == teamId)
                .Select(g => new
                {
                    g.Week, TeamId = g.LoserId, TeamName = g.LoserName,
                    OpponentId = g.WinnerId, OpponentName = g.WinnerName,
                    TeamPoints = g.LPoints, OpponentPoints = g.WPoints,
                    Delta = g.LPoints - g.WPoints, Result = "L", g.Location,
                    IsHomeTeam = g.Location == 'L',
                    LocationDisplay = g.Location == 'L' ? "Home" : g.Location == 'W' ? "Away" : "Neutral"
                });

            var games = await gamesFromWinner.Union(gamesFromLoser).OrderBy(g => g.Week).ToListAsync(token);

            var teamRecords    = await _context.TeamRecords.Where(tr => tr.Year == targetYear).ToDictionaryAsync(tr => tr.TeamID, token);
            var winsLookup     = teamRecords.ToDictionary(kvp => kvp.Key, kvp => (int)kvp.Value.Wins);
            var lossesLookup   = teamRecords.ToDictionary(kvp => kvp.Key, kvp => (int)kvp.Value.Losses);
            var avgScoreDeltas = await _uow.Lookups.GetAvgScoreDeltasAsync(token);
            var hfa            = _config.HomeFieldAdvantage;

            var analysis = games.Select(g =>
            {
                var teamWins  = winsLookup.GetValueOrDefault(g.TeamId, 0);
                var teamLosses = lossesLookup.GetValueOrDefault(g.TeamId, 0);
                var oppWins   = winsLookup.GetValueOrDefault(g.OpponentId, 0);
                var oppLosses = lossesLookup.GetValueOrDefault(g.OpponentId, 0);

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

                    if (g.IsHomeTeam)      { expectedFromTeam += hfa; homeAdjustment =  hfa; }
                    else if (g.Location != 'N') { expectedFromTeam -= hfa; homeAdjustment = -hfa; }

                    zScore = (g.Delta - expectedFromTeam) / (double)asd.StDevP;
                }

                var baseExpected     = teamWins >= (int)(oppWins) ? expectedDelta : -expectedDelta;
                var adjustedExpected = baseExpected + homeAdjustment;

                return (object)new
                {
                    g.Week, g.OpponentName, Location = g.LocationDisplay, g.Result, g.Delta,
                    TeamFinalWins         = teamWins, OppFinalWins = oppWins,
                    BaseExpectedDelta     = Math.Round(baseExpected,     1),
                    HomeAdjustment        = Math.Round(homeAdjustment,   1),
                    AdjustedExpectedDelta = Math.Round(adjustedExpected, 1),
                    ActualDelta           = g.Delta,
                    Difference            = Math.Round(g.Delta - adjustedExpected, 1),
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
                teamRecord?.CombinedSOS,
                Math.Round(avgZScore, 4), teamRecord?.PowerRating,
                Math.Round(avgZScore * (double)(teamRecord?.CombinedSOS ?? 1.0m), 4),
                analysis);
        }

        public async Task<TrendsResult> CalculateTrendsAsync(int? teamId, int? year, CancellationToken token)
        {
            var targetYear = year ?? DateTime.Now.Year;

            var query = _context.TeamRecords
                .Include(tr => tr.Team)
                .Where(tr => tr.Year == targetYear && tr.PowerRating != null);

            if (teamId.HasValue)
                query = query.Where(tr => tr.TeamID == teamId.Value);

            var records = await query.ToListAsync(token);

            var trends = records.Select(tr => (object)new
            {
                TeamId               = tr.TeamID,
                TeamName             = tr.Team?.TeamName,
                tr.Year, Record = $"{tr.Wins}-{tr.Losses}",
                tr.PowerRating, tr.CombinedSOS, tr.Ranking,
                WinPercentage        = (decimal)tr.Wins / (tr.Wins + tr.Losses),
                ProjectedFinalRanking = tr.Ranking,
                Trend                = tr.PowerRating > 0.02m ? "Ascending"
                                     : tr.PowerRating < -0.02m ? "Descending"
                                     : "Stable"
            }).ToList();

            return new TrendsResult(targetYear, trends.Count, trends);
        }

        public async Task<DiagnosticScoreDeltaResult> DiagnosticScoreDeltasAsync(int? year, CancellationToken token)
        {
            var targetYear = year ?? DateTime.Now.Year;

            var games       = await _uow.Games.GetByYearAsync(targetYear, token);
            var teamRecords = await _context.TeamRecords.Where(tr => tr.Year == targetYear).ToDictionaryAsync(tr => tr.TeamID, token);

            var results = games.Select(g =>
            {
                var winnerRecord = teamRecords.GetValueOrDefault(g.WinnerId);
                var loserRecord  = teamRecords.GetValueOrDefault(g.LoserId);
                var winnerWins   = winnerRecord?.Wins ?? 0;
                var loserWins    = loserRecord?.Wins  ?? 0;

                return (object)new
                {
                    g.WinnerName, WinnerWins = winnerWins, WinnerPoints = g.WPoints,
                    g.LoserName,  LoserWins  = loserWins,  LoserPoints  = g.LPoints,
                    IsUpset  = winnerWins < loserWins,
                    Team1Wins = winnerWins >= loserWins ? winnerWins : loserWins,
                    Team2Wins = winnerWins >= loserWins ? loserWins  : winnerWins,
                    Delta    = winnerWins >= loserWins ? g.WPoints - g.LPoints : g.LPoints - g.WPoints,
                    Explanation = winnerWins >= loserWins
                        ? $"Normal: {winnerWins}-win team beat {loserWins}-win team by {g.WPoints - g.LPoints}"
                        : $"UPSET: {winnerWins}-win team beat {loserWins}-win team, delta = {g.LPoints - g.WPoints}"
                };
            }).ToList();

            var upsetCount    = results.Count(r => ((dynamic)r).IsUpset);
            var negativeCount = results.Count(r => ((dynamic)r).Delta < 0);

            return new DiagnosticScoreDeltaResult(
                targetYear, results.Count, upsetCount, negativeCount, upsetCount > 0,
                upsetCount > 0 && negativeCount == 0 ? "Logic error: upsets exist but no negative deltas!" : null,
                results.Take(20));
        }

        // ── Weekly rankings ───────────────────────────────────────────────────────

        public async Task<WeeklyRankingsBackfillResult> BackfillWeeklyRankingsAsync(int? startYear, CancellationToken token)
        {
            var query = _context.Game.Where(g => g.WPoints > 0 || g.LPoints > 0);
            if (startYear.HasValue)
                query = query.Where(g => g.Year >= startYear.Value);

            var yearWeeks = await query
                .Select(g => new { g.Year, g.Week })
                .Distinct()
                .OrderBy(g => g.Year).ThenBy(g => g.Week)
                .ToListAsync(token);

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
    }
}
