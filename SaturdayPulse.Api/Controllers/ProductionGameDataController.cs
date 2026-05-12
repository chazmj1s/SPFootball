using Microsoft.AspNetCore.Mvc;
using SaturdayPulse.Contracts.Requests;
using SaturdayPulse.Services;

namespace SaturdayPulse.Controllers
{
    /// <summary>
    /// Production API for game predictions and team data queries.
    ///
    /// All data-access and business logic lives in ProductionGameDataService.
    /// This controller is a thin HTTP wrapper: validate input, call the service,
    /// map results to HTTP responses.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ProductionGameDataController(
        ProductionGameDataService gameDataService,
        ILogger<ProductionGameDataController> logger) : ControllerBase
    {
        #region Predictions

        /// <summary>
        /// Predicts the score for a single matchup between two teams.
        /// Location: 'H' = team is home, 'A' = team is away, 'N' = neutral site.
        /// Example: GET /api/productiongamedata/predictMatchup?year=2025&teamName=Ohio State&opponentName=Michigan&location=H&week=12
        /// </summary>
        [HttpGet("predictMatchup")]
        public async Task<IActionResult> PredictMatchup(
            [FromQuery] int? year,
            [FromQuery] string teamName,
            [FromQuery] string opponentName,
            [FromQuery] char location = 'N',
            [FromQuery] int week = 0)
        {
            if (string.IsNullOrEmpty(teamName) || string.IsNullOrEmpty(opponentName))
                return BadRequest("Both teamName and opponentName are required.");

            try
            {
                var targetYear = year ?? DateTime.Now.Year;
                var prediction = await gameDataService.PredictMatchupAsync(
                    targetYear, teamName, opponentName, location, week);

                return Ok(new
                {
                    matchup             = $"{prediction.TeamName} {prediction.LocationDisplay} {prediction.OpponentName}",
                    prediction          = $"{prediction.TeamName} {prediction.PredictedTeamScore:F1}, {prediction.OpponentName} {prediction.PredictedOpponentScore:F1}",
                    expectedMargin      = prediction.ExpectedMargin,
                    marginOfError       = prediction.MarginOfError,
                    confidence          = prediction.Confidence,
                    teamRecord          = $"{prediction.TeamWins}-?",
                    opponentRecord      = $"{prediction.OpponentWins}-?",
                    teamPowerRating     = prediction.TeamPowerRating,
                    opponentPowerRating = prediction.OpponentPowerRating,
                    rivalryNote         = prediction.RivalryNote,
                    summary             = prediction.PredictionSummary
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error predicting matchup");
                return StatusCode(500, ex.Message);
            }
        }

        /// <summary>
        /// Predicts scores for multiple matchups provided in the request body.
        /// </summary>
        [HttpPost("predictMatchups")]
        public async Task<IActionResult> PredictMatchups(
            [FromBody] MatchupBatchRequest request,
            CancellationToken token = default)
        {
            try
            {
                var predictions = await gameDataService.PredictMatchupsAsync(
                    request.Year, request.Matchups, token);

                return Ok(new
                {
                    message     = $"Predicted {predictions.Count} matchups for {request.Year}",
                    predictions = predictions.Select(p => new
                    {
                        matchup        = $"{p.TeamName} {p.LocationDisplay} {p.OpponentName}",
                        prediction     = $"{p.TeamName} {p.PredictedTeamScore:F1}, {p.OpponentName} {p.PredictedOpponentScore:F1}",
                        expectedMargin = p.ExpectedMargin,
                        marginOfError  = p.MarginOfError,
                        confidence     = p.Confidence,
                        rivalryNote    = p.RivalryNote,
                        summary        = p.PredictionSummary
                    })
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error predicting matchups");
                return StatusCode(500, ex.Message);
            }
        }

        #endregion

        #region Diagnostics and Queries

        /// <summary>
        /// Diagnostic endpoint to check database data availability.
        /// GET /api/productiongamedata/diagnostic
        /// </summary>
        [HttpGet("diagnostic")]
        public async Task<IActionResult> GetDiagnostic(CancellationToken token = default)
        {
            try
            {
                var result = await gameDataService.GetDiagnosticAsync(token);
                return Ok(new
                {
                    database               = result.Database,
                    totalTeams             = result.TotalTeams,
                    totalGames             = result.TotalGames,
                    totalRecords           = result.TotalRecords,
                    recordsWithPowerRating = result.RecordsWithPowerRating,
                    yearsWithData          = result.YearsWithData,
                    yearStats              = result.YearStats
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting diagnostic info");
                return StatusCode(500, ex.Message);
            }
        }

        /// <summary>
        /// Query team records with filters.
        /// Example: GET /api/productiongamedata/queryTeamRecords?wins=13&losses=3
        /// </summary>
        [HttpGet("queryTeamRecords")]
        public async Task<IActionResult> QueryTeamRecords(
            [FromQuery] int? wins,
            [FromQuery] int? losses,
            [FromQuery] int? minWins,
            [FromQuery] int? maxWins,
            [FromQuery] int? startYear,
            [FromQuery] int? endYear,
            [FromQuery] decimal? minPowerRating,
            [FromQuery] decimal? maxPowerRating,
            [FromQuery] int limit = 50,
            CancellationToken token = default)
        {
            try
            {
                var result = await gameDataService.QueryTeamRecordsAsync(
                    wins, losses, minWins, maxWins, startYear, endYear,
                    minPowerRating, maxPowerRating, limit, token);

                return Ok(new { count = result.Count, filters = result.Filters, results = result.Results });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error querying team records");
                return StatusCode(500, "An error occurred while querying team records.");
            }
        }

        /// <summary>
        /// Returns Seed/Trend/Pedigree ratings for all FBS teams in the given year.
        /// Example: GET /api/productiongamedata/rollingAverages?year=2025
        /// </summary>
        [HttpGet("rollingAverages")]
        public async Task<IActionResult> GetRollingAverages(
            [FromQuery] int? year,
            CancellationToken token = default)
        {
            try
            {
                var result = await gameDataService.GetRollingAveragesAsync(year, token);
                return Ok(new { year = result.Year, teamCount = result.TeamCount, rankings = result.Rankings });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching rolling averages for year={Year}", year);
                return StatusCode(500, "An error occurred fetching rolling averages.");
            }
        }

        /// <summary>
        /// Returns Seed/Trend/Pedigree ratings for a single team across all years.
        /// Example: GET /api/productiongamedata/rollingAverages/team?teamId=123
        /// </summary>
        [HttpGet("rollingAverages/team")]
        public async Task<IActionResult> GetTeamRollingAverages(
            [FromQuery] int teamId,
            [FromQuery] int? startYear,
            CancellationToken token = default)
        {
            try
            {
                var result = await gameDataService.GetTeamRollingAveragesAsync(teamId, startYear, token);
                return Ok(new
                {
                    teamId     = result.TeamId,
                    teamName   = result.TeamName,
                    conference = result.Conference,
                    history    = result.History
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching rolling averages for teamId={TeamId}", teamId);
                return StatusCode(500, "An error occurred fetching team rolling averages.");
            }
        }

        #endregion

        #region Rankings

        /// <summary>
        /// Query matchup histories and detected rivalries.
        /// Example: GET /api/productiongamedata/rivalries?tier=EPIC&minGames=50
        /// </summary>
        [HttpGet("rivalries")]
        public async Task<IActionResult> GetRivalries(
            [FromQuery] string? tier,
            [FromQuery] int? minGames,
            [FromQuery] double? minVarianceRatio,
            CancellationToken token = default)
        {
            try
            {
                var result = await gameDataService.GetRivalriesAsync(tier, minGames, minVarianceRatio, token);
                return Ok(new
                {
                    totalMatchups   = result.TotalMatchups,
                    totalInDatabase = result.TotalInDatabase,
                    filters         = result.Filters,
                    rivalries       = result.Rivalries
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error querying rivalries");
                return StatusCode(500, "An error occurred while querying rivalries.");
            }
        }

        /// <summary>
        /// Get power rankings for a specific year, optionally through a specific week.
        /// Example: GET /api/productiongamedata/powerrankings?year=2025
        /// Example: GET /api/productiongamedata/powerrankings?year=2025&throughWeek=10
        /// </summary>
        [HttpGet("powerrankings")]
        public async Task<IActionResult> GetPowerRankings(
            [FromQuery] int? year,
            [FromQuery] int? throughWeek,
            CancellationToken token = default)
        {
            try
            {
                var result = await gameDataService.GetPowerRankingsAsync(year, throughWeek, token);


                logger.LogInformation(
                    "Returned {Count} {Type} rankings for year {Year}{Week}",
                    result.Rankings.Count,
                    result.IsWeekly ? "weekly" : "season",
                    year ?? DateTime.Now.Year,
                    throughWeek.HasValue ? $" week {throughWeek}" : "");

                return Ok(result.Rankings);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving power rankings");
                return StatusCode(500, "An error occurred while retrieving power rankings.");
            }
        }

        #endregion

        #region Schedule

        /// <summary>
        /// Get the full schedule for a season with actual and projected scores/O-U.
        /// Example: GET /api/productiongamedata/schedule?year=2025
        /// </summary>
        [HttpGet("schedule")]
        public async Task<IActionResult> GetSchedule(
            [FromQuery] int? year,
            CancellationToken token = default)
        {
            try
            {
                var result = await gameDataService.GetScheduleAsync(year, token);
                return Ok(result.Games);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving schedule");
                return StatusCode(500, "An error occurred while retrieving the schedule.");
            }
        }

        #endregion

        #region Teams and Rivalries

        /// <summary>
        /// Returns all FBS teams with id, name, short name, conference, and tier.
        /// Example: GET /api/productiongamedata/teams
        /// </summary>
        [HttpGet("teams")]
        public async Task<IActionResult> GetTeams(CancellationToken token = default)
        {
            try
            {
                var result = await gameDataService.GetTeamsAsync(token);
                return Ok(result.Teams);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving teams");
                return StatusCode(500, "An error occurred while retrieving teams.");
            }
        }

        /// <summary>
        /// Returns only named rivalries with team names and series stats.
        /// Example: GET /api/productiongamedata/rivalries/named
        /// </summary>
        [HttpGet("rivalries/named")]
        public async Task<IActionResult> GetNamedRivalries(CancellationToken token = default)
        {
            try
            {
                var result = await gameDataService.GetNamedRivalriesAsync(token);
                return Ok(result.Rivalries);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving named rivalries");
                return StatusCode(500, "An error occurred while retrieving named rivalries.");
            }
        }

        /// <summary>
        /// Returns yearly rank/rating/SOS history for a team.
        /// Example: GET /api/productiongamedata/teamhistory?teamId=47&years=10
        /// </summary>
        [HttpGet("teamhistory")]
        public async Task<IActionResult> GetTeamHistory(
            [FromQuery] int teamId,
            [FromQuery] int years = 10,
            CancellationToken token = default)
        {
            try
            {
                var result = await gameDataService.GetTeamHistoryAsync(teamId, years, token);
                return Ok(new
                {
                    TeamId         = result.TeamId,
                    TeamName       = result.TeamName,
                    ShortName      = result.ShortName,
                    ConferenceAbbr = result.ConferenceAbbr,
                    History        = result.History
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving team history for {TeamId}", teamId);
                return StatusCode(500, "An error occurred while retrieving team history.");
            }
        }

        /// <summary>
        /// Returns yearly game results for a rivalry matchup.
        /// Example: GET /api/productiongamedata/rivalryhistory?team1Id=12&team2Id=47&years=10
        /// </summary>
        [HttpGet("rivalryhistory")]
        public async Task<IActionResult> GetRivalryHistory(
            [FromQuery] int team1Id,
            [FromQuery] int team2Id,
            [FromQuery] int years = 10,
            CancellationToken token = default)
        {
            try
            {
                var result = await gameDataService.GetRivalryHistoryAsync(team1Id, team2Id, years, token);
                return Ok(new
                {
                    Team1Id               = result.Team1Id,
                    Team1Name             = result.Team1Name,
                    Team1ShortName        = result.Team1ShortName,
                    Team2Id               = result.Team2Id,
                    Team2Name             = result.Team2Name,
                    Team2ShortName        = result.Team2ShortName,
                    RivalryName           = result.RivalryName,
                    RivalryTier           = result.RivalryTier,
                    GamesPlayed           = result.GamesPlayed,
                    AvgMargin             = result.AvgMargin,
                    UpsetRate             = result.UpsetRate,
                    History               = result.History,
                    CurrentYearProjection = result.CurrentYearProjection
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving rivalry history for {T1} vs {T2}", team1Id, team2Id);
                return StatusCode(500, "An error occurred while retrieving rivalry history.");
            }
        }

        #endregion

        #region Conference Standings and Projections

        /// <summary>
        /// Returns the projected conference championship game qualifiers for all FBS conferences.
        /// Example: GET /api/productiongamedata/championship-qualifiers?year=2025
        /// </summary>
        [HttpGet("championship-qualifiers")]
        public async Task<IActionResult> GetChampionshipQualifiers(
            [FromQuery] int? year,
            CancellationToken token = default)
        {
            try
            {
                var result = await gameDataService.GetChampionshipQualifiersAsync(year, token);
                return Ok(result.Conferences);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error computing championship qualifiers");
                return StatusCode(500, "An error occurred computing championship qualifiers.");
            }
        }

        /// <summary>
        /// Returns projected conference championship qualifiers combining actual results
        /// through the specified week with projections for remaining games.
        /// Example: GET /api/productiongamedata/projected-championship-qualifiers?year=2025&throughWeek=8
        /// </summary>
        [HttpGet("projected-championship-qualifiers")]
        public async Task<IActionResult> GetProjectedChampionshipQualifiers(
            [FromQuery] int? year,
            [FromQuery] int? throughWeek,
            CancellationToken token = default)
        {
            try
            {
                var result = await gameDataService.GetProjectedChampionshipQualifiersAsync(year, throughWeek, token);
                return Ok(result.Conferences);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error computing projected championship qualifiers");
                return StatusCode(500, "An error occurred computing projected championship qualifiers.");
            }
        }

        /// <summary>
        /// Returns projected final conference standings for all FBS teams.
        /// Example: GET /api/productiongamedata/projected-standings?year=2025&throughWeek=8&conference=SEC
        /// </summary>
        [HttpGet("projected-standings")]
        public async Task<IActionResult> GetProjectedStandings(
            [FromQuery] int? year,
            [FromQuery] int? throughWeek,
            [FromQuery] string? conference = null,
            CancellationToken token = default)
        {
            try
            {
                var result = await gameDataService.GetProjectedStandingsAsync(year, throughWeek, conference, token);
                return Ok(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error computing projected standings");
                return StatusCode(500, "An error occurred computing projected standings.");
            }
        }

        #endregion
    }
}
