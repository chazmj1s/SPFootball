using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Contracts.Responses;
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
        ProjectionAccuracyService _projectionAccuracyService,
        ILogger<DeveloperController> logger) : ControllerBase
    {
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
        /// Rebuilds AvgScoreDifferentials using strength differential buckets.
        /// Example:
        /// POST /api/developer/buildAvgScoreDifferentials?startYear=2010
        /// </summary>
        [HttpPost("buildAvgScoreDifferentials")]
        [Tags("Score Deltas and Rivalries")]
        public async Task<IActionResult> BuildAvgScoreDifferentials(
            [FromQuery] int startYear = 1965,
            CancellationToken token = default)
        {
            try
            {
                var result = await developerService
                    .BuildAvgScoreDifferentialsAsync(
                        startYear,
                        token);

                return Ok(new
                {
                    message = "AvgScoreDifferentials rebuilt successfully",
                    startYear,
                    rowsCreated = result
                });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error rebuilding AvgScoreDifferentials from startYear={StartYear}",
                    startYear);

                return StatusCode(
                    500,
                    "An error occurred while rebuilding AvgScoreDifferentials.");
            }
        }

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

        #region Season Initialization

        /// <summary>
        /// Initializes a new season by creating a week 0 WeeklyRankings snapshot
        /// seeded from the prior year's final week. Provides the preseason baseline
        /// for week 1 projections and initial TeamRecords.
        /// Safe to run multiple times — skips if week 0 already exists.
        /// Example: POST /api/developer/initializeSeason?year=2026
        /// </summary>
        [HttpPost("initializeSeason")]
        [Tags("Season Initialization")]
        public async Task<IActionResult> InitializeSeason(
            [FromQuery] int year,
            CancellationToken token = default)
        {
            try
            {
                var result = await developerService.InitializeSeasonAsync(year, token);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error initializing season {Year}", year);
                return StatusCode(500, "An error occurred while initializing the season.");
            }
        }

        /// <summary>
        /// Backfills week 0 snapshots for all years missing one.
        /// Safe to run multiple times — skips already-initialized years.
        /// Run before backfillWeeklyRankings.
        /// Example: POST /api/developer/backfillInitializeSeasons
        /// Example: POST /api/developer/backfillInitializeSeasons?startYear=2020
        /// </summary>
        [HttpPost("backfillInitializeSeasons")]
        [Tags("Season Initialization")]
        public async Task<IActionResult> BackfillInitializeSeasons(
            [FromQuery] int? startYear,
            CancellationToken token = default)
        {
            try
            {
                var result = await developerService.BackfillInitializeSeasonsAsync(startYear, token);
                return Ok(new
                {
                    message   = result.Message,
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
                logger.LogError(ex, "Error during season initialization backfill");
                return StatusCode(500, "An error occurred during season initialization backfill.");
            }
        }

        #endregion

        #region Analytics and Diagnostics

        [HttpGet("simulatePortalWeights")]
        [Tags("Analytics and Diagnostics")]
        public async Task<IActionResult> SimulatePortalWeights(
            [FromQuery] int? startYear,
            [FromQuery] int? endYear,
            CancellationToken token = default)
        {
            try
            {
                var result = await _projectionAccuracyService
                    .SimulatePortalWeightsAsync(startYear, endYear, token);
                return Ok(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error simulating portal weights");
                return StatusCode(500, "An error occurred simulating portal weights.");
            }
        }

        [HttpGet("portalAccuracy")]
        [Tags("Analytics and Diagnostics")]
        public async Task<IActionResult> GetPortalAccuracy(
            [FromQuery] int? startYear,
            [FromQuery] int? endYear,
            CancellationToken token = default)
        {
            try
            {
                var result = await _projectionAccuracyService
                    .ComputePortalAccuracyAsync(startYear, endYear, token);
                return Ok(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error computing portal accuracy");
                return StatusCode(500, "An error occurred computing portal accuracy.");
            }
        }

        /// <summary>
        /// Computes projection accuracy metrics vs actual game results.
        /// Optionally scoped to a year range. Includes MAE, winner accuracy,
        /// spread bias, and Vegas comparison where line data is available.
        /// Example: GET /api/developer/projectionAccuracy
        /// Example: GET /api/developer/projectionAccuracy?startYear=2015&endYear=2025
        /// </summary>
        [HttpGet("projectionAccuracy")]
        [Tags("Analytics and Diagnostics")]
        public async Task<IActionResult> GetProjectionAccuracy(
            [FromQuery] int? startYear,
            [FromQuery] int? endYear,
            CancellationToken token = default)
        {
            try
            {
                var result = await _projectionAccuracyService.ComputeAccuracyAsync(
                    startYear, endYear, token);
                return Ok(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error computing projection accuracy");
                return StatusCode(500, "An error occurred computing projection accuracy.");
            }
        }

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
        #region Portal

        /// <summary>
        /// Loads transfer portal entries for a single season from CFBD.
        /// Portal data is reliable from 2021 onward.
        /// Example: POST /api/developer/loadPortal?season=2026
        /// </summary>
        [HttpPost("loadPortal")]
        [Tags("Portal")]
        public async Task<IActionResult> LoadPortal(
            [FromQuery] int season,
            CancellationToken token = default)
        {
            try
            {
                var count = await developerService.LoadPortalAsync(season, token);
                return Ok(new { message = $"Portal entries loaded for {season}", count });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error loading portal for season={Season}", season);
                return StatusCode(500, "An error occurred while loading portal data.");
            }
        }

        /// <summary>
        /// Loads portal entries for every season from startSeason to current.
        /// Example: POST /api/developer/loadPortalBulk?startSeason=2021
        /// </summary>
        [HttpPost("loadPortalBulk")]
        [Tags("Portal")]
        public async Task<IActionResult> LoadPortalBulk(
            [FromQuery] int startSeason,
            CancellationToken token = default)
        {
            try
            {
                var total = await developerService.LoadPortalBulkAsync(startSeason, token);
                return Ok(new { message = $"Portal bulk load complete from {startSeason}", total });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error bulk loading portal from startSeason={StartSeason}", startSeason);
                return StatusCode(500, "An error occurred during portal bulk load.");
            }
        }

        /// <summary>
        /// Computes RosterStrength and PortalDelta for a single season and persists to TeamRecords.
        /// Run after loadPortal for the season.
        /// Example: POST /api/developer/computePortalMetrics?season=2026
        /// </summary>
        [HttpPost("computePortalMetrics")]
        [Tags("Portal")]
        public async Task<IActionResult> ComputePortalMetrics(
            [FromQuery] int season,
            CancellationToken token = default)
        {
            try
            {
                var count = await developerService.ComputePortalMetricsAsync(season, token);
                return Ok(new { message = $"Portal metrics computed for {season}", teamsUpdated = count });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error computing portal metrics for season={Season}", season);
                return StatusCode(500, "An error occurred computing portal metrics.");
            }
        }

        /// <summary>
        /// Computes RosterStrength and PortalDelta for all seasons with portal data.
        /// Example: POST /api/developer/computePortalMetricsBulk
        /// </summary>
        [HttpPost("computePortalMetricsBulk")]
        [Tags("Portal")]
        public async Task<IActionResult> ComputePortalMetricsBulk(CancellationToken token = default)
        {
            try
            {
                var total = await developerService.ComputePortalMetricsBulkAsync(token);
                return Ok(new { message = "Portal metrics computed for all seasons", teamsUpdated = total });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error bulk computing portal metrics");
                return StatusCode(500, "An error occurred computing portal metrics.");
            }
        }

        #endregion    
        #region Postseason Tagging

        /// <summary>
        /// Tags the specified games as SeasonType = "playoff" (CFP games).
        /// Called from the admin console postseason tagging page.
        /// Example: POST /api/developer/tagAsPlayoff
        /// Body: { "gameIds": [401628123, 401628124] }
        /// </summary>
        [HttpPost("tagAsPlayoff")]
        [Tags("Postseason Tagging")]
        public async Task<IActionResult> TagAsPlayoff(
            [FromBody] GameSeasonTypeRequest request,
            CancellationToken token = default)
        {
            if (request?.GameIds == null || request.GameIds.Count == 0)
                return BadRequest("At least one gameId is required.");

            try
            {
                var count = await developerService.SetSeasonTypeAsync(request.GameIds, "playoff", token);
                return Ok(new { message = $"{count} game(s) tagged as playoff", gamesUpdated = count });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error tagging games as playoff");
                return StatusCode(500, "An error occurred while tagging playoff games.");
            }
        }

        /// <summary>
        /// Reverts the specified games from SeasonType = "playoff" back to "postseason".
        /// Example: POST /api/developer/untagAsPlayoff
        /// Body: { "gameIds": [401628123] }
        /// </summary>
        [HttpPost("untagAsPlayoff")]
        [Tags("Postseason Tagging")]
        public async Task<IActionResult> UntagAsPlayoff(
            [FromBody] GameSeasonTypeRequest request,
            CancellationToken token = default)
        {
            if (request?.GameIds == null || request.GameIds.Count == 0)
                return BadRequest("At least one gameId is required.");

            try
            {
                var count = await developerService.SetSeasonTypeAsync(request.GameIds, "postseason", token);
                return Ok(new { message = $"{count} game(s) reverted to postseason", gamesUpdated = count });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error reverting games from playoff");
                return StatusCode(500, "An error occurred while reverting playoff games.");
            }
        }

        #endregion
    }
}
