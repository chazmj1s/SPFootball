using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Interfaces;
using SaturdayPulse.Services;

namespace SaturdayPulse.Controllers
{
    /// <summary>
    /// Development-only API for data loading, metric calculations, and diagnostics.
    /// NOT FOR PRODUCTION USE — these endpoints modify database state.
    ///
    /// All data-access and business logic lives in DeveloperService.
    /// This controller is a thin HTTP wrapper: validate input, call the service,
    /// map results to HTTP responses.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class DeveloperController(
        DeveloperService developerService,
        ILogger<DeveloperController> logger) : ControllerBase
    {
        #region Legacy Data Loading
        // TODO: Remove this region once CFBD V2 load is validated.

        /// <summary>
        /// Extracts game data starting from the provided year via web scraping.
        /// Example: GET /api/developer/initialGamesExtract?startYear=2020
        /// </summary>
        [HttpGet("initialGamesExtract")]
        [Tags("Legacy")]
        public async Task<IActionResult> InitialGamesExtract([FromQuery] int? startYear)
        {
            try
            {
                var result = await developerService.ExtractGameDataHistoryAsync(startYear);
                return Ok(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error extracting game data for startYear={StartYear}", startYear);
                return StatusCode(500, "An error occurred while extracting game data.");
            }
        }

        /// <summary>
        /// Loads game data from text files in the NCAA Raw Game Data directory.
        /// Example: GET /api/developer/loadGameHistoryFromFiles
        /// </summary>
        [HttpGet("loadGameHistoryFromFiles")]
        [Tags("Legacy")]
        public async Task<IActionResult> LoadGameHistoryFromFiles()
        {
            try
            {
                var result = await developerService.LoadGameHistoryFromFilesAsync();
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
        [Tags("Legacy")]
        public async Task<IActionResult> ProcessSingleFile(
            [FromQuery] string filePath,
            CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(filePath))
                return BadRequest("File path is required.");

            try
            {
                var recordsProcessed = await developerService.ProcessSingleFileAsync(filePath, token);
                return Ok(new
                {
                    message          = $"Successfully processed file: {Path.GetFileName(filePath)}",
                    recordsProcessed,
                    filePath
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
        /// Updates game data for a specific year and week from a local file,
        /// then recalculates TeamRecords and weekly metrics.
        /// Example: POST /api/developer/updateWeekGamesFromFile?year=2024&week=10
        /// </summary>
        [HttpPost("updateWeekGamesFromFile")]
        [Tags("Legacy")]
        public async Task<IActionResult> UpdateWeekGamesFromFile(
            [FromQuery] int year,
            [FromQuery] int week,
            CancellationToken token = default)
        {
            try
            {
                var result = await developerService.UpdateWeekGamesFromFileAsync(year, week, token);
                return Ok(result);
            }
            catch (FileNotFoundException ex)
            {
                logger.LogError(ex, "File not found: {Year}.txt", year);
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating game data from file: year={Year}, week={Week}", year, week);
                return StatusCode(500, "An error occurred while updating game data from file.");
            }
        }

        /// <summary>
        /// Updates game data for a specific year and week by fetching fresh data from the web.
        /// Example: POST /api/developer/updateWeekGames?year=2024&week=10
        /// </summary>
        [HttpPost("updateWeekGames")]
        [Tags("Legacy")]
        public async Task<IActionResult> UpdateWeekGames([FromQuery] int year, [FromQuery] int week)
        {
            try
            {
                var result = await developerService.UpdateWeekGamesAsync(year, week);
                return Ok(result);
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
        [Tags("Legacy")]
        public IActionResult ListAvailableFiles()
        {
            try
            {
                var result = developerService.ListAvailableFiles();
                return Ok(new { directory = result.Directory, fileCount = result.FileCount, files = result.Files });
            }
            catch (DirectoryNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error listing available files");
                return StatusCode(500, "An error occurred while listing available files.");
            }
        }

        #endregion

        #region CFBD V2 — Load

        /// <summary>
        /// Assigns correct week numbers (17+) to postseason games for a single year.
        /// CFBD returns week=1 for all postseason games; this fixes it by bucketing on game date.
        /// Example: POST /api/developer/assignPostseasonWeeks?year=2024
        /// </summary>
        [HttpPost("assignPostseasonWeeks")]
        [Tags("CFBD V2 - Load")]
        public async Task<IActionResult> AssignPostseasonWeeks(
            [FromQuery] int year,
            CancellationToken token = default)
        {
            try
            {
                var count = await developerService.AssignPostseasonWeeksAsync(year, token);
                return Ok(new { message = $"Postseason weeks assigned for {year}", gamesUpdated = count });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error assigning postseason weeks for year={Year}", year);
                return StatusCode(500, "An error occurred while assigning postseason weeks.");
            }
        }

        /// <summary>
        /// Bulk version — assigns correct postseason week numbers for every year from startYear to current.
        /// Run once to fix all historical week=1 postseason games.
        /// Example: POST /api/developer/assignPostseasonWeeksBulk?startYear=1963
        /// </summary>
        [HttpPost("assignPostseasonWeeksBulk")]
        [Tags("CFBD V2 - Load")]
        public async Task<IActionResult> AssignPostseasonWeeksBulk(
            [FromQuery] int startYear,
            CancellationToken token = default)
        {
            try
            {
                var count = await developerService.AssignPostseasonWeeksBulkAsync(startYear, token);
                return Ok(new { message = $"Postseason weeks assigned from {startYear} to current", gamesUpdated = count });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error bulk-assigning postseason weeks from startYear={StartYear}", startYear);
                return StatusCode(500, "An error occurred while bulk-assigning postseason weeks.");
            }
        }

        /// <summary>
        /// Fetches all conferences from CFBD and upserts into the Conferences table.
        /// Run once at season start or when conference realignment occurs.
        /// Example: POST /api/developer/loadConferences
        /// </summary>
        [HttpPost("loadConferences")]
        [Tags("CFBD V2 - Load")]
        public async Task<IActionResult> LoadConferences(CancellationToken token = default)
        {
            try
            {
                var count = await developerService.LoadConferencesAsync(token);
                return Ok(new { message = "Conferences loaded successfully", count });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error loading conferences from CFBD");
                return StatusCode(500, "An error occurred while loading conferences.");
            }
        }

        /// <summary>
        /// Fetches teams for a single year from CFBD and upserts into the Teams table.
        /// Omit year to default to the current season.
        /// Example: POST /api/developer/loadTeams?year=2025
        /// </summary>
        [HttpPost("loadTeams")]
        [Tags("CFBD V2 - Load")]
        public async Task<IActionResult> LoadTeams(
            [FromQuery] int? year,
            CancellationToken token = default)
        {
            try
            {
                var count = await developerService.LoadTeamsAsync(year, token);
                return Ok(new { message = "Teams loaded successfully", year = year ?? DateTime.Now.Year, count });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error loading teams from CFBD for year={Year}", year);
                return StatusCode(500, "An error occurred while loading teams.");
            }
        }

        /// <summary>
        /// Fetches teams for every year from startYear to current and upserts into the Teams table.
        /// Example: POST /api/developer/loadTeamsBulk?startYear=2000
        /// </summary>
        [HttpPost("loadTeamsBulk")]
        [Tags("CFBD V2 - Load")]
        public async Task<IActionResult> LoadTeamsBulk(
            [FromQuery] int startYear,
            CancellationToken token = default)
        {
            try
            {
                var total = await developerService.LoadTeamsBulkAsync(startYear, token);
                return Ok(new { message = "Bulk team load complete", startYear, total });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error bulk loading teams from CFBD starting year={StartYear}", startYear);
                return StatusCode(500, "An error occurred during bulk team load.");
            }
        }

        /// <summary>
        /// Fetches games for a single year (and optionally week) from CFBD and upserts into the Games table.
        /// Omit week to load the full season.
        /// Example: POST /api/developer/loadGames?year=2025
        /// Example: POST /api/developer/loadGames?year=2025&week=10
        /// </summary>
        [HttpPost("loadGames")]
        [Tags("CFBD V2 - Load")]
        public async Task<IActionResult> LoadGames(
            [FromQuery] int year,
            [FromQuery] int? week,
            CancellationToken token = default)
        {
            try
            {
                var count = await developerService.LoadGamesAsync(year, week, token);
                return Ok(new { message = "Games loaded successfully", year, week, count });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error loading games from CFBD for year={Year}, week={Week}", year, week);
                return StatusCode(500, "An error occurred while loading games.");
            }
        }

        /// <summary>
        /// Fetches games for every year from startYear to current and upserts into the Games table.
        /// Example: POST /api/developer/loadGamesBulk?startYear=2000
        /// </summary>
        [HttpPost("loadGamesBulk")]
        [Tags("CFBD V2 - Load")]
        public async Task<IActionResult> LoadGamesBulk(
            [FromQuery] int startYear,
            CancellationToken token = default)
        {
            try
            {
                var total = await developerService.LoadGamesBulkAsync(startYear, token);
                return Ok(new { message = "Bulk game load complete", startYear, total });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error bulk loading games from CFBD starting year={StartYear}", startYear);
                return StatusCode(500, "An error occurred during bulk game load.");
            }
        }

        /// <summary>
        /// Fetches Vegas lines for a single year/week from CFBD and upserts into the Lines table.
        /// Example: POST /api/developer/loadLines?year=2025&week=10
        /// </summary>
        [HttpPost("loadLines")]
        [Tags("CFBD V2 - Load")]
        public async Task<IActionResult> LoadLines(
            [FromQuery] int year,
            [FromQuery] int week,
            CancellationToken token = default)
        {
            try
            {
                var count = await developerService.LoadLinesAsync(year, week, token);
                return Ok(new { message = "Lines loaded successfully", year, week, count });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error loading lines from CFBD for year={Year}, week={Week}", year, week);
                return StatusCode(500, "An error occurred while loading lines.");
            }
        }

        /// <summary>
        /// Fetches Vegas lines for every year/week from startYear to current and upserts into the Lines table.
        /// Lines only exist from ~2013 forward; earlier years return empty gracefully.
        /// Example: POST /api/developer/loadLinesBulk?startYear=2013
        /// </summary>
        [HttpPost("loadLinesBulk")]
        [Tags("CFBD V2 - Load")]
        public async Task<IActionResult> LoadLinesBulk(
            [FromQuery] int startYear,
            CancellationToken token = default)
        {
            try
            {
                var total = await developerService.LoadLinesBulkAsync(startYear, token);
                return Ok(new { message = "Bulk lines load complete", startYear, total });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error bulk loading lines from CFBD starting year={StartYear}", startYear);
                return StatusCode(500, "An error occurred during bulk lines load.");
            }
        }

        [HttpPost("buildTeamsConferenceHistory")]
        [Tags("CFBD V2 - Load")]
        public async Task<IActionResult> BuildTeamsConferenceHistory(
                [FromQuery] int startYear, CancellationToken token = default)
        {
            var result = await developerService.BuildTeamsConferenceHistoryAsync(startYear, token);
            return Ok(new { message = $"{result} conference changes recorded from {startYear}" });
        }

        /// <summary>
        /// Sunday/Wednesday refresh — loads games and lines for a single week.
        /// Use this for the regular in-season weekly data update.
        /// Example: POST /api/developer/weeklyRefresh?year=2025&week=10
        /// </summary>
        [HttpPost("weeklyRefresh")]
        [Tags("CFBD V2 - Load")]
        public async Task<IActionResult> WeeklyRefresh(
            [FromQuery] int year,
            [FromQuery] int week,
            CancellationToken token = default)
        {
            try
            {
                var total = await developerService.WeeklyRefreshAsync(year, week, token);
                return Ok(new { message = "Weekly refresh complete", year, week, recordsLoaded = total });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during weekly refresh for year={Year}, week={Week}", year, week);
                return StatusCode(500, "An error occurred during weekly refresh.");
            }
        }

        #endregion

        #region CFBD V2 — Preview (non-destructive)

        /// <summary>
        /// Previews team data from CFBD without writing to the database.
        /// Example: GET /api/developer/previewCfbdTeams?year=2026
        /// </summary>
        [HttpGet("previewCfbdTeams")]
        [Tags("CFBD V2 - Preview")]
        public async Task<IActionResult> PreviewCfbdTeams(
            [FromQuery] int? year,
            CancellationToken token = default)
        {
            try
            {
                var result = await developerService.PreviewCfbdTeamsAsync(year, token);
                return Ok(new
                {
                    year = year ?? DateTime.Now.Year,
                    teamCount = result.Count,
                    teams = result
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error previewing CFBD teams for year={Year}", year);
                return StatusCode(500, "An error occurred while previewing teams.");
            }
        }

        /// <summary>
        /// Previews game data from CFBD without writing to the database.
        /// Example: GET /api/developer/previewCfbdGames?year=2026&week=1
        /// </summary>
        [HttpGet("previewCfbdGames")]
        [Tags("CFBD V2 - Preview")]
        public async Task<IActionResult> PreviewCfbdGames(
            [FromQuery] int year,
            [FromQuery] int? week,
            CancellationToken token = default)
        {
            try
            {
                var result = await developerService.PreviewCfbdGamesAsync(year, week, token);
                return Ok(new
                {
                    year,
                    week,
                    gameCount = result.Count,
                    games = result
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error previewing CFBD games for year={Year}, week={Week}", year, week);
                return StatusCode(500, "An error occurred while previewing games.");
            }
        }

        #endregion

        #region Rolling Averages

        /// <summary>
        /// Backfills SeedRating, TrendRating, and PedigreeRating for all teams.
        /// Example: POST /api/developer/backfillRollingAverages?startYear=1975
        /// </summary>
        [HttpPost("backfillRollingAverages")]
        [Tags("Rolling Averages")]
        public async Task<IActionResult> BackfillRollingAverages(
            [FromQuery] int? startYear,
            CancellationToken token = default)
        {
            try
            {
                var result = await developerService.BackfillRollingAveragesAsync(startYear, token);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during rolling averages backfill");
                return StatusCode(500, "An error occurred during backfill.");
            }
        }

        /// <summary>
        /// Recalculates rolling averages for a single year/week.
        /// Example: POST /api/developer/calculateRollingAverages?year=2025&week=8
        /// Example: POST /api/developer/calculateRollingAverages?year=2025  (preseason)
        /// </summary>
        [HttpPost("calculateRollingAverages")]
        [Tags("Rolling Averages")]
        public async Task<IActionResult> CalculateRollingAverages(
            [FromQuery] int? year,
            [FromQuery] int? week,
            CancellationToken token = default)
        {
            try
            {
                var result = await developerService.CalculateRollingAveragesAsync(year, week, token);
                return Ok(result);
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
        [Tags("Team Records and Metrics")]
        public async Task<IActionResult> UpdateTeamRecords([FromQuery] int? year)
        {
            try
            {
                await developerService.UpdateTeamRecordsAsync(year);
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
        [Tags("Team Records and Metrics")]
        public async Task<IActionResult> SetSOS([FromQuery] int? year, [FromQuery] int? week)
        {
            try
            {
                await developerService.SetSOSAsync(year, week);
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
        /// Example: POST /api/developer/calculatePowerRatings?year=2024
        /// </summary>
        [HttpPost("calculatePowerRatings")]   // was HttpGet — this modifies DB state
        [Tags("Team Records and Metrics")]
        public async Task<IActionResult> CalculatePowerRatings([FromQuery] int? year)
        {
            try
            {
                await developerService.CalculatePowerRatingsAsync(year);
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
        /// Example: POST /api/developer/calculateRankings?year=2024
        /// </summary>
        [HttpPost("calculateRankings")]   // was HttpGet — this modifies DB state
        [Tags("Team Records and Metrics")]
        public async Task<IActionResult> CalculateRankings([FromQuery] int? year)
        {
            try
            {
                var targetYear = year ?? DateTime.Now.Year;
                await developerService.CalculateRankingsAsync(targetYear);
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
        [Tags("Team Records and Metrics")]
        public async Task<IActionResult> UpdateWeeklyMetrics([FromQuery] int? year, [FromQuery] int? week)
        {
            try
            {
                var targetYear = year ?? DateTime.Now.Year;
                var targetWeek = week ?? 0;
                await developerService.RecalculateMetricsAsync(targetYear, targetWeek);
                return Ok(new
                {
                    message           = "Weekly metrics updated successfully",
                    year              = targetYear,
                    week              = targetWeek,
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
        [Tags("Team Records and Metrics")]
        public async Task<IActionResult> BackfillAllMetrics(
            [FromQuery] int? startYear,
            CancellationToken token = default)
        {
            try
            {
                var result = await developerService.BackfillAllMetricsAsync(startYear, token);
                return Ok(new
                {
                    message        = result.Message,
                    yearsProcessed = result.Processed,
                    startYear      = result.StartYear
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
        [Tags("Score Deltas and Rivalries")]
        public async Task<IActionResult> RecalculateScoreDeltas(CancellationToken token = default)
        {
            try
            {
                var result = await developerService.RecalculateScoreDeltasAsync(token);
                return Ok(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error recalculating score deltas");
                return StatusCode(500, "An error occurred while recalculating score deltas.");
            }
        }

        /// <summary>
        /// Clears and recreates the AvgScoreDeltas table, then recalculates all buckets.
        /// Example: POST /api/developer/recreateAvgScoreDeltasTable
        /// </summary>
        [HttpPost("recreateAvgScoreDeltasTable")]
        [Tags("Score Deltas and Rivalries")]
        public async Task<IActionResult> RecreateAvgScoreDeltasTable(CancellationToken token = default)
        {
            try
            {
                var result = await developerService.RecreateAvgScoreDeltasTableAsync(token);
                return Ok(result);
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
        [Tags("Score Deltas and Rivalries")]
        public async Task<IActionResult> CalculateMatchupHistories()
        {
            try
            {
                var result = await developerService.CalculateMatchupHistoriesAsync();
                return Ok(result);
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
        [Tags("Analytics and Diagnostics")]
        public async Task<IActionResult> GetAnalytics(
            [FromQuery] int? startYear,
            [FromQuery] int? endYear,
            CancellationToken token = default)
        {
            try
            {
                var result = await developerService.GetAnalyticsAsync(startYear, endYear, token);
                return Ok(new
                {
                    totalRecords       = result.TotalRecords,
                    yearRange          = result.YearRange,
                    overperformers     = result.Overperformers,
                    underperformers    = result.Underperformers,
                    averagePowerRating = result.AveragePowerRating,
                    averageSOS         = result.AverageSOS
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
        [Tags("Analytics and Diagnostics")]
        public async Task<IActionResult> AnalyzeTeamGames(
            [FromQuery] int teamId,
            [FromQuery] int? year,
            CancellationToken token = default)
        {
            try
            {
                var result = await developerService.AnalyzeTeamGamesAsync(teamId, year, token);
                return Ok(new
                {
                    result.TeamId,
                    result.Year,
                    record                = result.Record,
                    combinedSOS           = result.CombinedSOS,
                    avgZScore             = result.AvgZScore,
                    powerRating           = result.PowerRating,
                    calculatedPowerRating = result.CalculatedPowerRating,
                    games                 = result.Games
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
        [Tags("Analytics and Diagnostics")]
        public async Task<IActionResult> CalculateTrends(
            [FromQuery] int? teamId,
            [FromQuery] int? year,
            CancellationToken token = default)
        {
            try
            {
                var result = await developerService.CalculateTrendsAsync(teamId, year, token);
                return Ok(new { year = result.Year, teamCount = result.TeamCount, trends = result.Trends });
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
        [Tags("Analytics and Diagnostics")]
        public async Task<IActionResult> DiagnosticScoreDeltas(
            [FromQuery] int? year,
            CancellationToken token = default)
        {
            try
            {
                var result = await developerService.DiagnosticScoreDeltasAsync(year, token);
                return Ok(new
                {
                    year                = result.Year,
                    totalGames          = result.TotalGames,
                    upsetCount          = result.UpsetCount,
                    negativeDeltas      = result.NegativeDeltas,
                    shouldHaveNegatives = result.ShouldHaveNegatives,
                    problem             = result.Problem,
                    sampleGames         = result.SampleGames
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
        /// Example: POST /api/developer/backfillWeeklyRankings
        /// Example: POST /api/developer/backfillWeeklyRankings?startYear=2010
        /// </summary>
        [HttpPost("backfillWeeklyRankings")]
        [Tags("Analytics and Diagnostics")]
        public async Task<IActionResult> BackfillWeeklyRankings(
            [FromQuery] int? startYear,
            CancellationToken token = default)
        {
            try
            {
                var result = await developerService.BackfillWeeklyRankingsAsync(startYear, token);
                return Ok(new { message = result.Message, processed = result.Processed, startYear = result.StartYear });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during WeeklyRankings backfill");
                return StatusCode(500, "An error occurred during backfill.");
            }
        }

        /// <summary>
        /// Backfills the Projections table for every year/week combination in the database,
        /// starting from 1965 (or the provided startYear).
        ///
        /// For each week snapshot, projects all remaining regular-season games
        /// using the ratings that existed at that point in time (i.e. the WeeklyRankings
        /// power ratings for that year/week, not current ratings).
        ///
        /// Long-running — expect several minutes for a full 61-year backfill.
        /// Safe to re-run; rows are upserted on (GameId, Year, Week).
        ///
        /// Example: POST /api/developer/backfillProjections
        /// Example: POST /api/developer/backfillProjections?startYear=2010
        /// </summary>
        [HttpPost("backfillProjections")]
        [Tags("Analytics and Diagnostics")]
        public async Task<IActionResult> BackfillProjections(
            [FromQuery] int? startYear,
            CancellationToken token = default)
        {
            try
            {
                var result = await developerService.BackfillProjectionsAsync(startYear, token);
                return Ok(new
                {
                    message = result.Message,
                    processed = result.Processed,
                    startYear = result.StartYear
                });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during Projections backfill");
                return StatusCode(500, "An error occurred during projections backfill.");
            }
        }


        /// <summary>
        /// Computes and saves WeeklyRankings for a specific year/week, or backfills an entire year.
        /// Example: POST /api/developer/computeweekly?year=2025&week=10
        /// Example: POST /api/developer/computeweekly?year=2025&backfill=true
        /// </summary>
        [HttpPost("computeweekly")]
        [Tags("Analytics and Diagnostics")]
        public async Task<IActionResult> ComputeWeekly(
            [FromQuery] int? year,
            [FromQuery] int? week,
            [FromQuery] bool backfill = false,
            CancellationToken token = default)
        {
            try
            {
                var result = await developerService.ComputeWeeklyAsync(year, week, backfill, token);
                return Ok(new { message = result.Message, year = result.Year, week = result.Week });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error computing weekly rankings for year={Year}, week={Week}", year, week);
                return StatusCode(500, "An error occurred computing weekly rankings.");
            }
        }

        #endregion
    }
}
