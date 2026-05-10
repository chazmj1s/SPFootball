using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NCAA_Power_Ratings.Configuration;
using NCAA_Power_Ratings.Data;
using NCAA_Power_Ratings.Interfaces;
using NCAA_Power_Ratings.Models;
using NCAA_Power_Ratings.Services;
using NCAA_Power_Ratings.Utilities;

namespace NCAA_Power_Ratings.Controllers
{
    /// <summary>
    /// Development-only API for data loading, metric calculations, and diagnostics.
    /// NOT FOR PRODUCTION USE - These endpoints modify database state.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class DeveloperController(
        IGameDataService gameDataService,
        TeamMetricsService teamMetrics,
        RollingAverageService rollingAverageService,
        IDbContextFactory<NCAAContext> contextFactory,
        ScoreDeltaCalculator scoreDeltaCalculator,
        MatchupHistoryCalculator matchupHistoryCalculator,
        IOptions<MetricsConfiguration> config,
        WeeklyRankingsService weeklyRankingsService,
        ILogger<DeveloperController> logger) : ControllerBase
    {
        private readonly MetricsConfiguration _config = config.Value;
        private readonly WeeklyRankingsService _weeklyRankingsService = weeklyRankingsService;
        private readonly RollingAverageService _rollingAverageService = rollingAverageService;

        #region Data Loading Endpoints

        /// <summary>
        /// Extract game data starting from the provided year up through the current year via web scraping.
        /// Example: GET /api/developer/initialGamesExtract?startYear=2020
        /// </summary>
        [HttpGet("initialGamesExtract")]
        public async Task<ActionResult<List<Game>>> InitialGamesExtract([FromQuery] int? startYear)
        {
            try
            {
                var result = await gameDataService.ExtractGameDataHistoryAsync(startYear);
                return Ok(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error extracting game data for startYear={StartYear}", startYear);
                return StatusCode(500, "An error occurred while extracting game data.");
            }
        }

        /// <summary>
        /// Load game data for last 60 years from text files in NCAA Raw Game Data directory.
        /// Example: GET /api/developer/loadGameHistoryFromFiles
        /// </summary>
        [HttpGet("loadGameHistoryFromFiles")]
        public async Task<IActionResult> LoadGameHistoryFromFiles()
        {
            try
            {
                var result = await gameDataService.LoadGameHistoryFromFiles();
                return Ok(new { message = "Game history loaded successfully", recordsProcessed = result });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error loading game data from files");
                return StatusCode(500, "An error occurred while loading game data.");
            }
        }

        /// <summary>
        /// Processes a single file from the NCAA Raw Game Data directory.
        /// Example: POST /api/developer/processSingleFile?filePath=D:\NCAA Raw Game Data\2024.txt
        /// </summary>
        [HttpPost("processSingleFile")]
        public async Task<IActionResult> ProcessSingleFile([FromQuery] string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    return BadRequest("File path is required.");
                }

                var recordsProcessed = await gameDataService.ProcessSingleFileAsync(filePath);

                return Ok(new
                {
                    message = $"Successfully processed file: {Path.GetFileName(filePath)}",
                    recordsProcessed = recordsProcessed,
                    filePath = filePath
                });
            }
            catch (FileNotFoundException ex)
            {
                logger.LogError(ex, "File not found: {FilePath}", filePath);
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing file: {FilePath}", filePath);
                return StatusCode(500, "An error occurred while processing the file.");
            }
        }

        /// <summary>
        /// Updates game data for a specific year and week from a local file.
        /// Automatically recalculates TeamRecords and weekly metrics.
        /// Example: POST /api/developer/updateWeekGamesFromFile?year=2024&week=10
        /// </summary>
        [HttpPost("updateWeekGamesFromFile")]
        public async Task<IActionResult> UpdateWeekGamesFromFile([FromQuery] int year, [FromQuery] int week)
        {
            try
            {
                var fileName = $"{year}.txt";
                var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "NCAA Raw Game Data");
                var filePath = Path.Combine(dataDirectory, fileName);

                if (!System.IO.File.Exists(filePath))
                {
                    return NotFound($"File '{fileName}' not found in NCAA Raw Game Data directory.");
                }

                var gamesProcessed = await gameDataService.UpdateGameDataFromFileAsync(filePath, year, week, CancellationToken.None);

                await gameDataService.UpdateTeamRecordsAsync(year);
                await RecalculateMetricsAsync(year, week);

                var metricsMessage = "TeamRecords, SOS, PowerRating, and Ranking recalculated";

                return Ok(new
                {
                    message = $"Successfully processed games for {year} week {week} from file",
                    gamesProcessed = gamesProcessed,
                    metricsCalculated = metricsMessage,
                    year = year,
                    week = week,
                    sourceFile = fileName
                });
            }
            catch (FileNotFoundException ex)
            {
                logger.LogError(ex, "File not found: {Year}.txt", year);
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating game data from file: {Year}.txt, year={Year}, week={Week}", year, year, week);
                return StatusCode(500, "An error occurred while updating game data from file.");
            }
        }

        /// <summary>
        /// Updates game data for a specific year and week by fetching fresh data from the web.
        /// Example: POST /api/developer/updateWeekGames?year=2024&week=10
        /// NOTE: Keep separate from updateWeekGamesFromFile until a reliable live data
        /// source is confirmed. Previous source blacklisted the server after bulk scrape.
        /// </summary>
        [HttpPost("updateWeekGames")]
        public async Task<IActionResult> UpdateWeekGames([FromQuery] int year, [FromQuery] int week)
        {
            try
            {
                var gamesProcessed = await gameDataService.UpdateGameDataForYearAndWeekAsync(year, week);
                await gameDataService.UpdateTeamRecordsAsync(year);
                await RecalculateMetricsAsync(year, week);

                return Ok(new
                {
                    message = $"Successfully updated games for {year} week {week}",
                    gamesProcessed = gamesProcessed,
                    year = year,
                    week = week
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating week games: year={Year}, week={Week}", year, week);
                return StatusCode(500, "An error occurred while updating week games.");
            }
        }

        /// <summary>
        /// Lists available game data files in the NCAA Raw Game Data directory.
        /// Example: GET /api/developer/listAvailableFiles
        /// </summary>
        [HttpGet("listAvailableFiles")]
        public IActionResult ListAvailableFiles()
        {
            try
            {
                var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "NCAA Raw Game Data");

                if (!Directory.Exists(dataDirectory))
                {
                    return NotFound("NCAA Raw Game Data directory not found.");
                }

                var files = Directory.GetFiles(dataDirectory, "*.txt")
                    .Select(f => new
                    {
                        fileName = Path.GetFileName(f),
                        fullPath = f,
                        size = new FileInfo(f).Length,
                        lastModified = System.IO.File.GetLastWriteTime(f)
                    })
                    .OrderBy(f => f.fileName)
                    .ToList();

                return Ok(new
                {
                    directory = dataDirectory,
                    fileCount = files.Count,
                    files = files
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error listing available files");
                return StatusCode(500, "An error occurred while listing available files.");
            }
        }

        /// <summary>
        /// Backfills SeedRating, TrendRating, and PedigreeRating for all teams
        /// across every year from startYear to the current year.
        /// Run once after deploying the new columns.
        /// Example: POST /api/developer/backfillRollingAverages?startYear=1975
        /// </summary>
        [HttpPost("backfillRollingAverages")]
        public async Task<IActionResult> BackfillRollingAverages(
            [FromQuery] int? startYear,
            CancellationToken token = default)
        {
            try
            {
                await using var context = await contextFactory.CreateDbContextAsync(token);

                // Find all distinct years that have TeamRecords
                var yearsQuery = context.TeamRecords.Select(tr => (int)tr.Year).Distinct();

                if (startYear.HasValue)
                    yearsQuery = yearsQuery.Where(y => y >= startYear.Value);

                var years = await yearsQuery.OrderBy(y => y).ToListAsync(token);

                if (!years.Any())
                    return NotFound("No TeamRecords found matching the criteria.");

                logger.LogInformation(
                    "Backfilling rolling averages for {Count} years...", years.Count);

                int processed = 0;
                foreach (var year in years)
                {
                    // Backfill always uses week null (preseason / no live swap)
                    // so Seed is computed from prior seasons only for all historical years.
                    await _rollingAverageService.ComputeAndPersistAsync(year, week: null, token);
                    processed++;
                    logger.LogInformation(
                        "Rolling averages complete: {Year} ({Done}/{Total})",
                        year, processed, years.Count);
                }

                return Ok(new
                {
                    message = "Backfill complete.",
                    processed,
                    startYear
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during rolling averages backfill");
                return StatusCode(500, "An error occurred during backfill.");
            }
        }
        /// <summary>
        /// Recalculates rolling averages for a single year/week.
        /// Useful for testing the live swap behaviour without running the full pipeline.
        /// Example: POST /api/developer/calculateRollingAverages?year=2025&week=8
        /// Example: POST /api/developer/calculateRollingAverages?year=2025         (preseason)
        /// </summary>
        [HttpPost("calculateRollingAverages")]
        public async Task<IActionResult> CalculateRollingAverages(
            [FromQuery] int? year,
            [FromQuery] int? week,
            CancellationToken token = default)
        {
            try
            {
                var targetYear = year ?? DateTime.Now.Year;
                await _rollingAverageService.ComputeAndPersistAsync(targetYear, week, token);

                return Ok(new
                {
                    message = $"Rolling averages computed for {targetYear}" +
                              (week.HasValue ? $" week {week.Value}" : " (preseason)"),
                    year = targetYear,
                    week,
                    liveSwapActive = week.HasValue
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error calculating rolling averages: year={Year}, week={Week}", year, week);
                return StatusCode(500, "An error occurred calculating rolling averages.");
            }
        }


        #endregion

        #region Team Records and Metrics

        /// <summary>
        /// Rebuilds team records for the specified year (or all years if not specified).
        /// Example: POST /api/developer/updateTeamRecords?year=2024
        /// </summary>
        [HttpPost("updateTeamRecords")]
        public async Task<IActionResult> UpdateTeamRecords([FromQuery] int? year)
        {
            try
            {
                await gameDataService.UpdateTeamRecordsAsync(year);
                return Ok(new { message = $"Team records updated for {year?.ToString() ?? "all years"}" });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating team records for year={Year}", year);
                return StatusCode(500, "An error occurred while updating team records.");
            }
        }

        /// <summary>
        /// Calculates and sets Strength of Schedule (SOS) values for all teams.
        /// Example: POST /api/developer/setSOS?year=2024&week=10
        /// </summary>
        [HttpPost("setSOS")]
        public async Task<IActionResult> SetSOS([FromQuery] int? year, [FromQuery] int? week)
        {
            try
            {
                await teamMetrics.SetSOS(year, week);
                return Ok(new { message = $"SOS calculated for year {year ?? DateTime.Now.Year}, week {week ?? 0}" });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error calculating SOS for year={Year}, week={Week}", year, week);
                return StatusCode(500, "An error occurred while calculating SOS.");
            }
        }

        /// <summary>
        /// Calculates power ratings for a specific year.
        /// Example: GET /api/developer/calculatePowerRatings?year=2024
        /// </summary>
        [HttpGet("calculatePowerRatings")]
        public async Task<IActionResult> CalculatePowerRatings([FromQuery] int? year)
        {
            try
            {
                await teamMetrics.CalculatePowerRatings(year);
                return Ok(new { message = $"Power ratings calculated for {year ?? DateTime.Now.Year}" });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error calculating power ratings");
                return StatusCode(500, "An error occurred while calculating power ratings.");
            }
        }

        /// <summary>
        /// Calculates rankings for all teams in a specific year.
        /// Example: GET /api/developer/calculateRankings?year=2024
        /// </summary>
        [HttpGet("calculateRankings")]
        public async Task<IActionResult> CalculateRankings([FromQuery] int? year)
        {
            try
            {
                var targetYear = year ?? DateTime.Now.Year;
                await teamMetrics.CalculateRankings(targetYear);
                return Ok(new { message = $"Rankings calculated for {targetYear}" });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error calculating rankings");
                return StatusCode(500, "An error occurred while calculating rankings.");
            }
        }

        /// <summary>
        /// Calculates weekly metrics (SOS, PowerRating, Ranking) for a specific week.
        /// Example: POST /api/developer/updateWeeklyMetrics?year=2024&week=10
        /// </summary>
        [HttpPost("updateWeeklyMetrics")]
        public async Task<IActionResult> UpdateWeeklyMetrics([FromQuery] int? year, [FromQuery] int? week)
        {
            try
            {
                var targetYear = year ?? DateTime.Now.Year;
                var targetWeek = week ?? 0;

                await RecalculateMetricsAsync(targetYear, targetWeek);

                return Ok(new
                {
                    message = "Weekly metrics updated successfully",
                    year = targetYear,
                    week = targetWeek,
                    metricsCalculated = new[] { "SOS", "PowerRating", "Ranking" }
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating weekly metrics");
                return StatusCode(500, "An error occurred while updating weekly metrics.");
            }
        }

        /// <summary>
        /// Backfills all metrics (SOS, PowerRating, Ranking) for all years or from a start year.
        /// Example: POST /api/developer/backfillAllMetrics?startYear=2000
        /// </summary>
        [HttpPost("backfillAllMetrics")]
        public async Task<IActionResult> BackfillAllMetrics([FromQuery] int? startYear)
        {
            try
            {
                await using var context = await contextFactory.CreateDbContextAsync();

                var years = await context.TeamRecords
                    .Select(tr => tr.Year)
                    .Distinct()
                    .OrderBy(y => y)
                    .ToListAsync();

                if (startYear.HasValue)
                {
                    years = years.Where(y => y >= startYear.Value).ToList();
                }

                foreach (var year in years)
                {
                    logger.LogInformation("Processing year {Year}", year);
                    await RecalculateMetricsAsync((int)year, null);
                }

                return Ok(new
                {
                    message = "Backfill completed successfully",
                    yearsProcessed = years.Count,
                    startYear = years.FirstOrDefault(),
                    endYear = years.LastOrDefault()
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error backfilling metrics");
                return StatusCode(500, "An error occurred during backfill.");
            }
        }

        #endregion

        #region Score Delta and Rivalry Calculations

        /// <summary>
        /// Recalculates the AvgScoreDeltas table using 5% win-percentage buckets.
        /// Example: POST /api/developer/recalculateScoreDeltas
        /// </summary>
        [HttpPost("recalculateScoreDeltas")]
        public async Task<IActionResult> RecalculateScoreDeltas()
        {
            try
            {
                await scoreDeltaCalculator.UpdateAvgScoreDeltasTableAsync();

                await using var context = await contextFactory.CreateDbContextAsync();
                var count = await context.AvgScoreDeltas.CountAsync();

                return Ok(new
                {
                    message = "Score deltas recalculated successfully",
                    bucketsCreated = count,
                    bucketSystem = "5% win percentage increments",
                    nextStep = "Predictions will now use updated delta statistics"
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error recalculating score deltas");
                return StatusCode(500, "An error occurred while recalculating score deltas.");
            }
        }

        /// <summary>
        /// Drops and recreates the AvgScoreDeltas table, then recalculates all buckets.
        /// Example: POST /api/developer/recreateAvgScoreDeltasTable
        /// </summary>
        [HttpPost("recreateAvgScoreDeltasTable")]
        public async Task<IActionResult> RecreateAvgScoreDeltasTable()
        {
            try
            {
                await using var context = await contextFactory.CreateDbContextAsync();

                await context.Database.ExecuteSqlRawAsync("DELETE FROM AvgScoreDeltas");
                logger.LogInformation("AvgScoreDeltas table cleared");

                await scoreDeltaCalculator.UpdateAvgScoreDeltasTableAsync();

                var count = await context.AvgScoreDeltas.CountAsync();

                return Ok(new
                {
                    message = "AvgScoreDeltas table recreated successfully",
                    bucketsCreated = count,
                    action = "Table cleared and repopulated"
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error recreating AvgScoreDeltas table");
                return StatusCode(500, "An error occurred while recreating the table.");
            }
        }

        /// <summary>
        /// Calculates matchup histories for all 50 curated rivalries.
        /// Example: POST /api/developer/calculateMatchupHistories
        /// </summary>
        [HttpPost("calculateMatchupHistories")]
        public async Task<IActionResult> CalculateMatchupHistories()
        {
            try
            {
                var count = await matchupHistoryCalculator.CalculateAllMatchupHistories();

                return Ok(new
                {
                    message = "Matchup histories calculated successfully",
                    matchupsCreated = count,
                    rivalriesProcessed = 50,
                    nextStep = "Matchup-specific variance will now be used in predictions"
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error calculating matchup histories");
                return StatusCode(500, "An error occurred while calculating matchup histories.");
            }
        }

        #endregion

        #region Analytics and Diagnostics

        /// <summary>
        /// Provides detailed analytics on team performance vs calculated metrics.
        /// Example: GET /api/developer/analytics?startYear=2020&endYear=2024
        /// </summary>
        [HttpGet("analytics")]
        public async Task<IActionResult> GetAnalytics([FromQuery] int? startYear, [FromQuery] int? endYear)
        {
            try
            {
                await using var context = await contextFactory.CreateDbContextAsync();

                var query = context.TeamRecords
                    .Include(tr => tr.Team)
                    .Where(tr => tr.PowerRating != null);

                if (startYear.HasValue)
                    query = query.Where(tr => tr.Year >= startYear.Value);

                if (endYear.HasValue)
                    query = query.Where(tr => tr.Year <= endYear.Value);

                var records = await query.ToListAsync();

                var overperformers = records
                    .Where(tr => tr.Wins > (tr.CombinedSOS ?? 0) * 12)
                    .OrderByDescending(tr => tr.Wins - (tr.CombinedSOS ?? 0) * 12)
                    .Take(10)
                    .Select(tr => new
                    {
                        tr.Year,
                        TeamName = tr.Team?.TeamName,
                        Record = $"{tr.Wins}-{tr.Losses}",
                        tr.CombinedSOS,
                        tr.PowerRating,
                        Overperformance = tr.Wins - (tr.CombinedSOS ?? 0) * 12
                    });

                var underperformers = records
                    .Where(tr => tr.Wins < (tr.CombinedSOS ?? 0) * 12)
                    .OrderBy(tr => tr.Wins - (tr.CombinedSOS ?? 0) * 12)
                    .Take(10)
                    .Select(tr => new
                    {
                        tr.Year,
                        TeamName = tr.Team?.TeamName,
                        Record = $"{tr.Wins}-{tr.Losses}",
                        tr.CombinedSOS,
                        tr.PowerRating,
                        Underperformance = (tr.CombinedSOS ?? 0) * 12 - tr.Wins
                    });

                return Ok(new
                {
                    totalRecords = records.Count,
                    yearRange = $"{startYear ?? records.Min(r => r.Year)}-{endYear ?? records.Max(r => r.Year)}",
                    overperformers,
                    underperformers,
                    averagePowerRating = records.Average(r => (double?)r.PowerRating),
                    averageSOS = records.Average(r => (double?)r.CombinedSOS)
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error generating analytics");
                return StatusCode(500, "An error occurred while generating analytics.");
            }
        }

        /// <summary>
        /// Shows detailed game-by-game analysis for a specific team.
        /// Example: GET /api/developer/analyzeTeamGames?teamId=110&year=2024
        /// </summary>
        [HttpGet("analyzeTeamGames")]
        public async Task<IActionResult> AnalyzeTeamGames([FromQuery] int teamId, [FromQuery] int? year)
        {
            try
            {
                await using var context = await contextFactory.CreateDbContextAsync();

                var targetYear = year ?? DateTime.Now.Year;

                var gamesFromWinner = context.Game
                    .Where(g => g.Year == targetYear && g.WinnerId == teamId)
                    .Select(g => new
                    {
                        g.Week,
                        TeamId = g.WinnerId,
                        TeamName = g.WinnerName,
                        OpponentId = g.LoserId,
                        OpponentName = g.LoserName,
                        TeamPoints = g.WPoints,
                        OpponentPoints = g.LPoints,
                        Delta = g.WPoints - g.LPoints,
                        Result = "W",
                        g.Location,
                        IsHomeTeam = g.Location == 'W',
                        LocationDisplay = g.Location == 'W' ? "Home" : g.Location == 'L' ? "Away" : "Neutral"
                    });

                var gamesFromLoser = context.Game
                    .Where(g => g.Year == targetYear && g.LoserId == teamId)
                    .Select(g => new
                    {
                        g.Week,
                        TeamId = g.LoserId,
                        TeamName = g.LoserName,
                        OpponentId = g.WinnerId,
                        OpponentName = g.WinnerName,
                        TeamPoints = g.LPoints,
                        OpponentPoints = g.WPoints,
                        Delta = g.LPoints - g.WPoints,
                        Result = "L",
                        g.Location,
                        IsHomeTeam = g.Location == 'L',
                        LocationDisplay = g.Location == 'L' ? "Home" : g.Location == 'W' ? "Away" : "Neutral"
                    });

                var games = await gamesFromWinner.Union(gamesFromLoser)
                    .OrderBy(g => g.Week)
                    .ToListAsync();

                var teamRecords = await context.TeamRecords
                    .Where(tr => tr.Year == targetYear)
                    .ToDictionaryAsync(tr => tr.TeamID);

                var winsLookup = teamRecords.ToDictionary(kvp => kvp.Key, kvp => (int)kvp.Value.Wins);
                var lossesLookup = teamRecords.ToDictionary(kvp => kvp.Key, kvp => (int)kvp.Value.Losses);

                var avgScoreDeltas = await context.AvgScoreDeltas.ToListAsync();

                var homeFieldAdvantage = _config.HomeFieldAdvantage;
                var analysis = games.Select(g =>
                {
                    var teamWins = winsLookup.GetValueOrDefault(g.TeamId, 0);
                    var teamLosses = lossesLookup.GetValueOrDefault(g.TeamId, 0);
                    var oppWins = winsLookup.GetValueOrDefault(g.OpponentId, 0);
                    var oppLosses = lossesLookup.GetValueOrDefault(g.OpponentId, 0);

                    var teamGamesPlayed = teamWins + teamLosses;
                    var oppGamesPlayed = oppWins + oppLosses;

                    var teamWinPct = teamGamesPlayed > 0
                        ? Math.Round((decimal)teamWins / teamGamesPlayed * 20m, MidpointRounding.AwayFromZero) / 20m
                        : 0m;
                    var oppWinPct = oppGamesPlayed > 0
                        ? Math.Round((decimal)oppWins / oppGamesPlayed * 20m, MidpointRounding.AwayFromZero) / 20m
                        : 0m;

                    var maxWinPct = Math.Max(teamWinPct, oppWinPct);
                    var minWinPct = Math.Min(teamWinPct, oppWinPct);

                    var delta = g.Delta;

                    var asd = avgScoreDeltas.FirstOrDefault(
                        a => a.Team1WinPct == maxWinPct && a.Team2WinPct == minWinPct);

                    double zScore = 0.0;
                    double expectedDelta = 0.0;
                    double homeAdjustment = 0.0;

                    if (asd != null && asd.StDevP != 0)
                    {
                        expectedDelta = (double)asd.AverageScoreDelta;

                        var expectedFromTeamPerspective = teamWinPct >= oppWinPct
                            ? expectedDelta
                            : -expectedDelta;

                        if (g.IsHomeTeam)
                        {
                            expectedFromTeamPerspective += homeFieldAdvantage;
                            homeAdjustment = homeFieldAdvantage;
                        }
                        else if (g.Location != 'N')
                        {
                            expectedFromTeamPerspective -= homeFieldAdvantage;
                            homeAdjustment = -homeFieldAdvantage;
                        }

                        zScore = (delta - expectedFromTeamPerspective) / (double)asd.StDevP;
                    }

                    var baseExpected = teamWins >= oppWins ? expectedDelta : -expectedDelta;
                    var adjustedExpected = baseExpected + homeAdjustment;

                    return new
                    {
                        g.Week,
                        g.OpponentName,
                        Location = g.LocationDisplay,
                        g.Result,
                        g.Delta,
                        TeamFinalWins = teamWins,
                        OppFinalWins = oppWins,
                        BaseExpectedDelta = Math.Round(baseExpected, 1),
                        HomeAdjustment = Math.Round(homeAdjustment, 1),
                        AdjustedExpectedDelta = Math.Round(adjustedExpected, 1),
                        ActualDelta = delta,
                        Difference = Math.Round(delta - adjustedExpected, 1),
                        ZScore = Math.Round(zScore, 3),
                        Performance = zScore > _config.DominantPerformanceThreshold ? "Dominant"
                                    : zScore > _config.UnderperformedThreshold ? "Expected"
                                    : "Underperformed"
                    };
                }).ToList();

                var avgZScore = analysis.Average(a => a.ZScore);
                var teamRecord = await context.TeamRecords
                    .Where(tr => tr.TeamID == teamId && tr.Year == targetYear)
                    .FirstOrDefaultAsync();

                return Ok(new
                {
                    teamId,
                    year = targetYear,
                    record = $"{teamRecord?.Wins}-{teamRecord?.Losses}",
                    combinedSOS = teamRecord?.CombinedSOS,
                    avgZScore = Math.Round(avgZScore, 4),
                    powerRating = teamRecord?.PowerRating,
                    calculatedPowerRating = Math.Round(avgZScore * (double)(teamRecord?.CombinedSOS ?? 1.0m), 4),
                    games = analysis
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error analyzing team games");
                return StatusCode(500, "An error occurred during analysis.");
            }
        }

        /// <summary>
        /// Calculates trend projections based on recent performance.
        /// Example: GET /api/developer/calculateTrends?teamId=110&year=2024
        /// </summary>
        [HttpGet("calculateTrends")]
        public async Task<IActionResult> CalculateTrends([FromQuery] int? teamId, [FromQuery] int? year)
        {
            try
            {
                await using var context = await contextFactory.CreateDbContextAsync();

                var targetYear = year ?? DateTime.Now.Year;

                var query = context.TeamRecords
                    .Include(tr => tr.Team)
                    .Where(tr => tr.Year == targetYear && tr.PowerRating != null);

                if (teamId.HasValue)
                {
                    query = query.Where(tr => tr.TeamID == teamId.Value);
                }

                var records = await query.ToListAsync();

                var trends = records.Select(tr => new
                {
                    TeamId = tr.TeamID,
                    TeamName = tr.Team?.TeamName,
                    tr.Year,
                    Record = $"{tr.Wins}-{tr.Losses}",
                    tr.PowerRating,
                    tr.CombinedSOS,
                    tr.Ranking,
                    WinPercentage = (decimal)tr.Wins / (tr.Wins + tr.Losses),
                    ProjectedFinalRanking = tr.Ranking,
                    Trend = tr.PowerRating > 0.02m ? "Ascending" :
                           tr.PowerRating < -0.02m ? "Descending" : "Stable"
                }).ToList();

                return Ok(new
                {
                    year = targetYear,
                    teamCount = trends.Count,
                    trends
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error calculating trends");
                return StatusCode(500, "An error occurred while calculating trends.");
            }
        }

        /// <summary>
        /// Diagnostic endpoint to verify score delta calculations and upset handling.
        /// Example: GET /api/developer/diagnosticScoreDeltas?year=2024
        /// </summary>
        [HttpGet("diagnosticScoreDeltas")]
        public async Task<IActionResult> DiagnosticScoreDeltas([FromQuery] int? year)
        {
            try
            {
                await using var context = await contextFactory.CreateDbContextAsync();

                var targetYear = year ?? DateTime.Now.Year;

                var games = await context.Game
                    .Where(g => g.Year == targetYear)
                    .ToListAsync();

                var teamRecords = await context.TeamRecords
                    .Where(tr => tr.Year == targetYear)
                    .ToDictionaryAsync(tr => tr.TeamID);

                var results = games.Select(g =>
                {
                    var winnerRecord = teamRecords.GetValueOrDefault(g.WinnerId);
                    var loserRecord = teamRecords.GetValueOrDefault(g.LoserId);

                    var winnerWins = winnerRecord?.Wins ?? 0;
                    var loserWins = loserRecord?.Wins ?? 0;

                    return new
                    {
                        g.WinnerName,
                        WinnerWins = winnerWins,
                        WinnerPoints = g.WPoints,
                        g.LoserName,
                        LoserWins = loserWins,
                        LoserPoints = g.LPoints,
                        IsUpset = winnerWins < loserWins,
                        Team1Wins = winnerWins >= loserWins ? winnerWins : loserWins,
                        Team2Wins = winnerWins >= loserWins ? loserWins : winnerWins,
                        Delta = winnerWins >= loserWins
                            ? g.WPoints - g.LPoints
                            : g.LPoints - g.WPoints,
                        Explanation = winnerWins >= loserWins
                            ? $"Normal: {winnerWins}-win team beat {loserWins}-win team by {g.WPoints - g.LPoints}"
                            : $"UPSET: {winnerWins}-win team beat {loserWins}-win team, delta from {loserWins}-win perspective = {g.LPoints - g.WPoints}"
                    };
                }).ToList();

                var upsetCount = results.Count(r => r.IsUpset);
                var negativeCount = results.Count(r => r.Delta < 0);

                return Ok(new
                {
                    year = targetYear,
                    totalGames = results.Count,
                    upsetCount,
                    negativeDeltas = negativeCount,
                    shouldHaveNegatives = upsetCount > 0,
                    problem = upsetCount > 0 && negativeCount == 0 ? "Logic error: upsets exist but no negative deltas!" : null,
                    sampleGames = results.Take(20)
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error running diagnostic");
                return StatusCode(500, "An error occurred during diagnostic.");
            }
        }

        /// <summary>
        /// Backfills WeeklyRankings for every year/week combination in the database.
        /// Loops through all distinct Year+Week pairs that have played games.
        /// Example: POST /api/developer/backfillWeeklyRankings
        /// Example: POST /api/developer/backfillWeeklyRankings?startYear=2010
        /// </summary>
        [HttpPost("backfillWeeklyRankings")]
        public async Task<IActionResult> BackfillWeeklyRankings(
            [FromQuery] int? startYear,
            CancellationToken token = default)
        {
            try
            {
                await using var context = await contextFactory.CreateDbContextAsync(token);

                var yearWeeksQuery = context.Game
                    .Where(g => g.WPoints > 0 || g.LPoints > 0);

                if (startYear.HasValue)
                    yearWeeksQuery = yearWeeksQuery.Where(g => g.Year >= startYear.Value);

                var yearWeeks = await yearWeeksQuery
                    .Select(g => new { g.Year, g.Week })
                    .Distinct()
                    .OrderBy(g => g.Year)
                    .ThenBy(g => g.Week)
                    .ToListAsync(token);

                if (!yearWeeks.Any())
                    return NotFound("No played games found matching the criteria.");

                logger.LogInformation(
                    "Backfilling WeeklyRankings for {Count} year/week combinations...",
                    yearWeeks.Count);

                int processed = 0;
                foreach (var yw in yearWeeks)
                {
                    await _weeklyRankingsService.ComputeAndSaveAsync(yw.Year, yw.Week, token);
                    processed++;
                    logger.LogInformation("Completed {Year} week {Week} ({Done}/{Total})",
                        yw.Year, yw.Week, processed, yearWeeks.Count);
                }

                return Ok(new
                {
                    message   = $"Backfill complete.",
                    processed = processed,
                    startYear = startYear
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during WeeklyRankings backfill");
                return StatusCode(500, "An error occurred during backfill.");
            }
        }

        /// <summary>
        /// Computes and saves WeeklyRankings for a specific year/week, or backfills
        /// an entire year. Moved here from ProductionGameDataController — admin only.
        /// Example: POST /api/developer/computeweekly?year=2025&week=10
        /// Example: POST /api/developer/computeweekly?year=2025&backfill=true
        /// </summary>
        [HttpPost("computeweekly")]
        public async Task<IActionResult> ComputeWeekly(
            [FromQuery] int? year,
            [FromQuery] int? week,
            [FromQuery] bool backfill = false,
            CancellationToken token = default)
        {
            try
            {
                var targetYear = year ?? DateTime.Now.Year;

                if (backfill)
                {
                    await _weeklyRankingsService.BackfillYearAsync(targetYear, token);
                    return Ok(new { message = $"Backfilled all weeks for {targetYear}." });
                }

                if (!week.HasValue)
                    return BadRequest("Provide week=N or backfill=true.");

                await _weeklyRankingsService.ComputeAndSaveAsync(targetYear, week.Value, token);

                return Ok(new
                {
                    message = $"Computed weekly rankings for {targetYear} week {week.Value}.",
                    year    = targetYear,
                    week    = week.Value
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error computing weekly rankings for year={Year}, week={Week}", year, week);
                return StatusCode(500, "An error occurred computing weekly rankings.");
            }
        }

        #endregion

        // ── Private helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Runs the full metrics pipeline in dependency order:
        ///   SetSOS → CalculatePowerRatings → CalculateRankings
        /// These three calls must always run sequentially — each reads what the
        /// previous one wrote to TeamRecords. Do not parallelize.
        /// </summary>
        private async Task RecalculateMetricsAsync(int year, int? week)
        {
            // Pipeline order is strict — each step reads what the previous wrote.
            await _rollingAverageService.ComputeAndPersistAsync(year, week);  // Seed/Trend/Pedigree → TeamRecords
            await teamMetrics.SetSOS(year, week);                             // reads SeedRating (week 0) or live wins (week 6+)
            await teamMetrics.CalculatePowerRatings(year);                    // reads CombinedSOS
            await teamMetrics.CalculateRankings(year);                        // reads PowerRating + CombinedSOS
        }
    }
}
