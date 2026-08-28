using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Api.Contracts.Responses;
using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.Core.Progress;
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
        RatingComparisonService _ratingComparisonService,
        UserProfileService userProfileService,
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
        /// Streaming version — yields one ProgressUpdate per year as it completes,
        /// instead of one response after the full range finishes. Used by the
        /// metrics-rebuild console for live progress.
        /// NOTE: no try/catch here — once the response starts streaming, an
        /// exception thrown before any item is yielded can't be turned into an
        /// HTTP error status (the 200 + opening bracket are already written).
        /// Per-item failures are caught inside the service and yielded as a failed
        /// ProgressUpdate instead; see LoadTeamsBulkStreamAsync.
        /// Example: POST /api/developer/loadTeamsBulk/stream?startYear=2000
        /// </summary>
        [HttpPost("loadTeamsBulk/stream")]
        [Tags("CFBD V2 - Load")]
        public IAsyncEnumerable<ProgressUpdate> LoadTeamsBulkStream(
            [FromQuery] int startYear,
            CancellationToken token) =>
            developerService.LoadTeamsBulkStreamAsync(startYear, token);

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

        /// <summary>Streaming version — see LoadTeamsBulkStream for the no-try/catch rationale.</summary>
        [HttpPost("loadGamesBulk/stream")]
        [Tags("CFBD V2 - Load")]
        public IAsyncEnumerable<ProgressUpdate> LoadGamesBulkStream(
            [FromQuery] int startYear,
            CancellationToken token) =>
            developerService.LoadGamesBulkStreamAsync(startYear, token);

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

        /// <summary>Streaming version — see LoadTeamsBulkStream for the no-try/catch rationale.</summary>
        [HttpPost("loadLinesBulk/stream")]
        [Tags("CFBD V2 - Load")]
        public IAsyncEnumerable<ProgressUpdate> LoadLinesBulkStream(
            [FromQuery] int startYear,
            CancellationToken token) =>
            developerService.LoadLinesBulkStreamAsync(startYear, token);

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

        // setSOS / calculatePowerRatings / calculateRankings / updateWeeklyMetrics /
        // backfillAllMetrics removed — TeamMetricsService deleted entirely.
        // See DeveloperService.cs for the reasoning. WeeklyRankingsService
        // (backfillWeeklyRankings / computeweekly) is the single source of truth
        // for SOS/PowerRating/Ranking now.

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
        /// <summary>
        /// Streaming — yields one ProgressUpdate per year as it's initialized.
        /// No try/catch: see LoadTeamsBulkStream's doc comment for why. Per-year
        /// failures are caught inside the service and yielded as a failed
        /// ProgressUpdate instead of aborting the stream.
        /// </summary>
        [HttpPost("backfillInitializeSeasons")]
        [Tags("Season Initialization")]
        public IAsyncEnumerable<ProgressUpdate> BackfillInitializeSeasons(
            [FromQuery] int? startYear,
            CancellationToken token) =>
            developerService.BackfillInitializeSeasonsStreamAsync(startYear, token);

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
        /// Method B — MOV Variance Test (two-parameter). Returns the fit without
        /// persisting anything — pass throughYear to test "as of a past season"
        /// behavior, or omit for the live default (everything played so far).
        /// Example: GET /api/developer/tierDiscountAnalysis?startYear=1965
        /// </summary>
        [HttpGet("tierDiscountAnalysis")]
        [Tags("Analytics and Diagnostics")]
        public async Task<IActionResult> GetTierDiscountAnalysis(
            [FromQuery] int startYear = 1965,
            [FromQuery] int? throughYear = null,
            CancellationToken token = default)
        {
            try
            {
                var result = await developerService.CalculateTierDiscountAsync(startYear, throughYear, token);
                return Ok(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error generating tier discount analysis");
                return StatusCode(500, "An error occurred while generating the tier discount analysis.");
            }
        }

        /// <summary>
        /// Computes and persists a new TierDiscountCoefficients row for `season`,
        /// using only games played through season - 1. Intended to run BEFORE
        /// Initialize Season in the Season Setup sequence. Returns a 200 with a
        /// "skipped" message (not an error) if there's no usable prior-year data yet.
        /// Example: POST /api/developer/computeTierDiscountCoefficients?season=2026
        /// </summary>
        [HttpPost("computeTierDiscountCoefficients")]
        [Tags("Analytics and Diagnostics")]
        public async Task<IActionResult> ComputeTierDiscountCoefficients(
            [FromQuery] int season,
            [FromQuery] int startYear = 1965,
            CancellationToken token = default)
        {
            try
            {
                var coefficient = await developerService.ComputeTierDiscountCoefficientsAsync(season, startYear, token);
                if (coefficient == null)
                {
                    return Ok(new { message = $"Skipped season {season} — no usable prior-year data yet.", persisted = false });
                }
                return Ok(new { message = $"Tier discount coefficients computed and persisted for season {season}.", persisted = true, coefficient });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error computing tier discount coefficients");
                return StatusCode(500, "An error occurred while computing tier discount coefficients.");
            }
        }

        /// <summary>
        /// Backfills TierDiscountCoefficients for every season from startSeason
        /// through the most recent season with played data (or throughSeason, if
        /// given).
        /// Example: POST /api/developer/computeTierDiscountCoefficientsBulk?startSeason=1965
        /// </summary>
        [HttpPost("computeTierDiscountCoefficientsBulk")]
        [Tags("Analytics and Diagnostics")]
        public async Task<IActionResult> ComputeTierDiscountCoefficientsBulk(
            [FromQuery] int startSeason,
            [FromQuery] int? throughSeason = null,
            [FromQuery] int startYear = 1965,
            CancellationToken token = default)
        {
            try
            {
                var (persisted, skipped) = await developerService.ComputeTierDiscountCoefficientsBulkAsync(startSeason, throughSeason, startYear, token);
                return Ok(new
                {
                    message = $"Tier discount coefficients backfilled — {persisted} season(s) persisted, {skipped} skipped (no usable prior-year data).",
                    persisted,
                    skipped
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error backfilling tier discount coefficients");
                return StatusCode(500, "An error occurred while backfilling tier discount coefficients.");
            }
        }


        /// <summary>
        /// Computes and persists a new AnchorBlendCoefficients row for `season`,
        /// using only games played through season - 1. Intended to run BEFORE
        /// Initialize Season in the Season Setup sequence. Returns a 200 with a
        /// "skipped" message (not an error) if there's no usable prior-year data yet.
        /// Example: POST /api/developer/computeAnchorBlendCoefficients?season=2026
        /// </summary>
        [HttpPost("computeAnchorBlendCoefficients")]
        [Tags("Analytics and Diagnostics")]
        public async Task<IActionResult> ComputeAnchorBlendCoefficients(
            [FromQuery] int season,
            [FromQuery] int windowYears = 3,
            CancellationToken token = default)
        {
            try
            {
                var coefficient = await developerService.ComputeAnchorBlendCoefficientsAsync(season, windowYears, token);
                if (coefficient == null)
                {
                    return Ok(new { message = $"Skipped season {season} — no usable prior-year data yet.", persisted = false });
                }
                return Ok(new { message = $"Anchor blend coefficients computed and persisted for season {season}.", persisted = true, coefficient });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error computing anchor blend coefficients");
                return StatusCode(500, "An error occurred while computing anchor blend coefficients.");
            }
        }

        /// <summary>
        /// Backfills AnchorBlendCoefficients for every season from startSeason
        /// through the most recent season with played data (or throughSeason, if
        /// given).
        /// Example: POST /api/developer/computeAnchorBlendCoefficientsBulk?startSeason=1965
        /// </summary>
        [HttpPost("computeAnchorBlendCoefficientsBulk")]
        [Tags("Analytics and Diagnostics")]
        public async Task<IActionResult> ComputeAnchorBlendCoefficientsBulk(
            [FromQuery] int startSeason,
            [FromQuery] int? throughSeason = null,
            [FromQuery] int windowYears = 3,
            CancellationToken token = default)
        {
            try
            {
                var (persisted, skipped) = await developerService.ComputeAnchorBlendCoefficientsBulkAsync(startSeason, throughSeason, windowYears, token);
                return Ok(new
                {
                    message = $"Anchor blend coefficients backfilled — {persisted} season(s) persisted, {skipped} skipped (no usable prior-year data).",
                    persisted,
                    skipped
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error backfilling anchor blend coefficients");
                return StatusCode(500, "An error occurred while backfilling anchor blend coefficients.");
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
        /// <summary>
        /// Streaming — yields one ProgressUpdate per year. This also fixes the
        /// earlier CancellationToken.None bug: the real request token is now
        /// threaded through instead of being discarded, so an aborted/timed-out
        /// client actually stops server-side work instead of it running to
        /// completion unobserved.
        /// </summary>
        [HttpPost("backfillWeeklyRankings")]
        [Tags("Analytics and Diagnostics")]
        public IAsyncEnumerable<ProgressUpdate> BackfillWeeklyRankings(
            [FromQuery] int? startYear,
            CancellationToken token) =>
            developerService.BackfillWeeklyRankingsStreamAsync(startYear, token);

        // backfillProjections endpoint removed — BackfillProjectionsStreamAsync
        // (old multi-snapshot-per-game Projections design, upserted on
        // (GameId, Year, Week)) is superseded by Option C in
        // WeeklyRankingsService.ComputeAndSaveAsync, which locks at most one
        // Projection row per GameId. Fully covered by backfillWeeklyRankings now.

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

        // <summary>
        /// EXPERIMENTAL — compares the production snapshot-cliff rating method against the
        /// K=4 inertia-blended alternative (RatingComparisonService), for every real
        /// scheduled game across the given week range. Returns predicted spread and O/U from
        /// both methods side by side, sorted by largest spread disagreement first.
        /// Read-only — writes nothing to the database.
        /// Example: GET /api/developer/compareRatingMethods?year=2025&startWeek=1&endWeek=12
        /// Example testing a candidate HFA: &hfaOverride=5.5
        /// </summary>
        [HttpGet("compareRatingMethods")]
        [Tags("Analytics and Diagnostics")]
        public async Task<IActionResult> CompareRatingMethods(
            [FromQuery] int year,
            [FromQuery] int startWeek,
            [FromQuery] int endWeek,
            [FromQuery] double? hfaOverride,
            CancellationToken token = default)
        {
            try
            {
                var weeks = Enumerable.Range(startWeek, endWeek - startWeek + 1);
                var result = await _ratingComparisonService.CompareAsync(year, weeks, hfaOverride, token);
                return Ok(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error comparing rating methods for year={Year}, weeks={StartWeek}-{EndWeek}",
                    year, startWeek, endWeek);
                return StatusCode(500, "An error occurred comparing rating methods.");
            }
        }

        /// <summary>
        /// EXPERIMENTAL — grades production and K=4-blended predictions against actual
        /// final scores for the given week range: the real question ("is K=4 more
        /// accurate"), not just "do the two methods disagree" (see compareRatingMethods).
        /// Read-only — writes nothing to the database.
        /// Example: GET /api/developer/compareRatingAccuracy?year=2025&startWeek=1&endWeek=14
        /// Example testing a candidate HFA: &hfaOverride=5.5 — check byLocation.Home.
        /// spreadBias afterward to see if it moved closer to zero for production/experimental.
        /// </summary>
        [HttpGet("compareRatingAccuracy")]
        [Tags("Analytics and Diagnostics")]
        public async Task<IActionResult> CompareRatingAccuracy(
            [FromQuery] int year,
            [FromQuery] int startWeek,
            [FromQuery] int endWeek,
            [FromQuery] double? hfaOverride,
            CancellationToken token = default)
        {
            try
            {
                var weeks = Enumerable.Range(startWeek, endWeek - startWeek + 1);
                var result = await _ratingComparisonService.CompareAccuracyAsync(year, weeks, hfaOverride, token);
                return Ok(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error comparing rating accuracy for year={Year}, weeks={StartWeek}-{EndWeek}",
                    year, startWeek, endWeek);
                return StatusCode(500, "An error occurred comparing rating accuracy.");
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

        #region Roster Capacity

        /// <summary>
        /// Fetches the roster for a single season from CFBD and upserts into RosterPlayers.
        /// Call once for the current year (T) and once for the prior year (T-1) —
        /// RosterCapacityService needs both snapshots.
        /// Example: POST /api/developer/loadRosterCapacityRoster?season=2026
        /// </summary>
        [HttpPost("loadRosterCapacityRoster")]
        [Tags("Roster Capacity")]
        public async Task<IActionResult> LoadRosterCapacityRoster(
            [FromQuery] int season,
            CancellationToken token = default)
        {
            try
            {
                var count = await developerService.LoadRosterCapacityRosterAsync(season, token);
                return Ok(new { message = $"Roster loaded for {season}", count });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error loading roster capacity roster for season={Season}", season);
                return StatusCode(500, "An error occurred while loading roster data.");
            }
        }

        /// <summary>
        /// Loads roster for both the current season and the prior season in one call.
        /// Example: POST /api/developer/loadRosterCapacityRosterBothSeasons?currentSeason=2026
        /// </summary>
        [HttpPost("loadRosterCapacityRosterBothSeasons")]
        [Tags("Roster Capacity")]
        public async Task<IActionResult> LoadRosterCapacityRosterBothSeasons(
            [FromQuery] int currentSeason,
            CancellationToken token = default)
        {
            try
            {
                var (currentCount, priorCount) = await developerService.LoadRosterCapacityBothSeasonsAsync(currentSeason, token);
                return Ok(new
                {
                    message = $"Roster loaded for {currentSeason} and {currentSeason - 1}",
                    currentCount,
                    priorCount
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error loading roster capacity roster for both seasons around currentSeason={CurrentSeason}", currentSeason);
                return StatusCode(500, "An error occurred while loading roster data.");
            }
        }

        /// <summary>
        /// Fetches player season stats for a single season from CFBD (bulk, no team filter)
        /// and upserts into PlayerStats. Used for T-1 to compute departed-player production shares.
        /// Example: POST /api/developer/loadRosterCapacityStats?season=2025
        /// </summary>
        [HttpPost("loadRosterCapacityStats")]
        [Tags("Roster Capacity")]
        public async Task<IActionResult> LoadRosterCapacityStats(
            [FromQuery] int season,
            CancellationToken token = default)
        {
            try
            {
                var count = await developerService.LoadRosterCapacityStatsAsync(season, token);
                return Ok(new { message = $"Player stats loaded for {season}", count });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error loading roster capacity stats for season={Season}", season);
                return StatusCode(500, "An error occurred while loading player stats.");
            }
        }

        /// <summary>
        /// Fetches head coaches for a single year from CFBD and upserts into CoachRecords.
        /// Used to detect year-over-year HC turnover for the coaching penalty.
        /// Example: POST /api/developer/loadRosterCapacityCoaches?year=2026
        /// </summary>
        [HttpPost("loadRosterCapacityCoaches")]
        [Tags("Roster Capacity")]
        public async Task<IActionResult> LoadRosterCapacityCoaches(
            [FromQuery] int year,
            CancellationToken token = default)
        {
            try
            {
                var count = await developerService.LoadRosterCapacityCoachesAsync(year, token);
                return Ok(new { message = $"Coach records loaded for {year}", count });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error loading roster capacity coaches for year={Year}", year);
                return StatusCode(500, "An error occurred while loading coach data.");
            }
        }

        /// <summary>
        /// Fetches the recruiting class for a single year from CFBD and upserts into
        /// RecruitPlayers. Filters out uncommitted recruits (no CommittedTo).
        /// Example: POST /api/developer/loadRosterCapacityRecruiting?year=2025
        /// </summary>
        [HttpPost("loadRosterCapacityRecruiting")]
        [Tags("Roster Capacity")]
        public async Task<IActionResult> LoadRosterCapacityRecruiting(
            [FromQuery] int year,
            CancellationToken token = default)
        {
            try
            {
                var count = await developerService.LoadRosterCapacityRecruitingAsync(year, token);
                return Ok(new { message = $"Recruiting class loaded for {year}", count });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error loading roster capacity recruiting for year={Year}", year);
                return StatusCode(500, "An error occurred while loading recruiting data.");
            }
        }

        /// <summary>
        /// Loads the recruiting class for a year and immediately joins RecruitRating into
        /// RosterPlayers for that same year. Requires that year's roster already loaded via
        /// loadRosterCapacityRoster or loadRosterCapacityRosterBothSeasons.
        /// Example: POST /api/developer/loadAndApplyRosterCapacityRecruiting?year=2025
        /// </summary>
        [HttpPost("loadAndApplyRosterCapacityRecruiting")]
        [Tags("Roster Capacity")]
        public async Task<IActionResult> LoadAndApplyRosterCapacityRecruiting(
            [FromQuery] int year,
            CancellationToken token = default)
        {
            try
            {
                var (recruitsLoaded, ratingsApplied) =
                    await developerService.LoadAndApplyRosterCapacityRecruitingAsync(year, token);
                return Ok(new
                {
                    message = $"Recruiting class loaded and applied for {year}",
                    recruitsLoaded,
                    ratingsApplied
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error loading/applying roster capacity recruiting for year={Year}", year);
                return StatusCode(500, "An error occurred while loading and applying recruiting data.");
            }
        }

        /// <summary>
        /// Loads transfer portal entries for a single season and immediately joins ratings
        /// into RosterPlayers.TransferRating for that same year. Requires that year's roster
        /// already loaded via loadRosterCapacityRoster or loadRosterCapacityRosterBothSeasons.
        /// Example: POST /api/developer/loadAndApplyPortalRatings?season=2025
        /// </summary>
        [HttpPost("loadAndApplyPortalRatings")]
        [Tags("Roster Capacity")]
        public async Task<IActionResult> LoadAndApplyPortalRatings(
            [FromQuery] int season,
            CancellationToken token = default)
        {
            try
            {
                var (portalLoaded, ratingsApplied) =
                    await developerService.LoadAndApplyPortalRatingsAsync(season, token);
                return Ok(new
                {
                    message = $"Portal data loaded and applied for {season}",
                    portalLoaded,
                    ratingsApplied
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error loading/applying portal ratings for season={Season}", season);
                return StatusCode(500, "An error occurred while loading and applying portal data.");
            }
        }

        /// <summary>
        /// Read-only coverage check — which seasons since portal data became
        /// available (2021) have zero PortalEntries rows. Safe to call any time;
        /// writes nothing.
        /// Example: GET /api/developer/portalCoverage
        /// </summary>
        [HttpGet("portalCoverage")]
        [Tags("Roster Capacity")]
        public async Task<IActionResult> GetPortalCoverage(CancellationToken token = default)
        {
            try
            {
                var result = await developerService.GetPortalCoverageAsync(token);
                return Ok(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error checking portal coverage");
                return StatusCode(500, "An error occurred while checking portal coverage.");
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

        #region Users

        /// <summary>
        /// All user profiles with their entitlements, for the admin console's
        /// Users page.
        /// Example: GET /api/developer/users
        /// </summary>
        [HttpGet("users")]
        [Tags("Users")]
        public async Task<IActionResult> GetUsers(CancellationToken token = default)
        {
            try
            {
                var users = await userProfileService.GetAllUsersWithEntitlementsAsync(token);
                return Ok(users);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving users");
                return StatusCode(500, "An error occurred while retrieving users.");
            }
        }

        /// <summary>
        /// Grants (or extends) beta access to a product for a user.
        /// Example: POST /api/developer/grantBetaAccess?userId=abc123&productKey=cfb-season-pass
        /// </summary>
        [HttpPost("grantBetaAccess")]
        [Tags("Users")]
        public async Task<IActionResult> GrantBetaAccess(
            [FromQuery] string userId, [FromQuery] string productKey, CancellationToken token = default)
        {
            try
            {
                await userProfileService.GrantBetaAccessAsync(userId, productKey, token);
                return Ok(new { message = $"Beta access granted for {productKey}" });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error granting beta access for {UserId}/{ProductKey}", userId, productKey);
                return StatusCode(500, "An error occurred while granting access.");
            }
        }

        /// <summary>
        /// Grants a season pass for a specific season year - ad hoc admin tool
        /// for special cases, distinct from grantBetaAccess. Always creates a
        /// new entitlement row (doesn't extend an existing one), so a user can
        /// accumulate multiple distinct season grants over time.
        /// Example: POST /api/developer/grantSeasonPass?userId=abc123&productKey=cfb-season-pass&season=2026
        /// </summary>
        [HttpPost("grantSeasonPass")]
        [Tags("Users")]
        public async Task<IActionResult> GrantSeasonPass(
            [FromQuery] string userId, [FromQuery] string productKey, [FromQuery] int season, CancellationToken token = default)
        {
            try
            {
                await userProfileService.GrantSeasonPassAsync(userId, productKey, season, token);
                return Ok(new { message = $"Season pass granted for {productKey}, season {season}" });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error granting season pass for {UserId}/{ProductKey}/{Season}", userId, productKey, season);
                return StatusCode(500, "An error occurred while granting the season pass.");
            }
        }

        /// <summary>
        /// Revokes a user's active entitlement for a product (expires it; does not delete the row).
        /// Example: POST /api/developer/revokeAccess?userId=abc123&productKey=cfb-season-pass
        /// </summary>
        [HttpPost("revokeAccess")]
        [Tags("Users")]
        public async Task<IActionResult> RevokeAccess(
            [FromQuery] string userId, [FromQuery] string productKey, CancellationToken token = default)
        {
            try
            {
                await userProfileService.RevokeAccessAsync(userId, productKey, token);
                return Ok(new { message = $"Access revoked for {productKey}" });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error revoking access for {UserId}/{ProductKey}", userId, productKey);
                return StatusCode(500, "An error occurred while revoking access.");
            }
        }

        #endregion
    }
}
