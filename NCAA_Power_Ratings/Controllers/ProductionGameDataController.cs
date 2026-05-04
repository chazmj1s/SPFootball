using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NCAA_Power_Ratings.Data;
using NCAA_Power_Ratings.Models;
using NCAA_Power_Ratings.Services;

namespace NCAA_Power_Ratings.Controllers
{
    /// <summary>
    /// Production API for game predictions and team data queries.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ProductionGameDataController(
    IDbContextFactory<NCAAContext> contextFactory,
    ILogger<ProductionGameDataController> logger,
    GamePredictionService predictionService) : ControllerBase
    {
        private readonly GamePredictionService _predictionService = predictionService;


        /// <summary>
        /// Predicts the score for a single matchup between two teams.
        /// Location: 'H' = team is home, 'A' = team is away, 'N' = neutral site
        /// 
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
            try
            {
                if (string.IsNullOrEmpty(teamName) || string.IsNullOrEmpty(opponentName))
                {
                    return BadRequest("Both teamName and opponentName are required");
                }

                var targetYear = year ?? DateTime.Now.Year;

                var prediction = await _predictionService.PredictMatchup(
                    targetYear, teamName, opponentName, location, week);

                return Ok(new
                {
                    matchup = $"{prediction.TeamName} {prediction.LocationDisplay} {prediction.OpponentName}",
                    prediction = $"{prediction.TeamName} {prediction.PredictedTeamScore:F1}, {prediction.OpponentName} {prediction.PredictedOpponentScore:F1}",
                    expectedMargin = prediction.ExpectedMargin,
                    marginOfError = prediction.MarginOfError,
                    confidence = prediction.Confidence,
                    teamRecord = $"{prediction.TeamWins}-?",
                    opponentRecord = $"{prediction.OpponentWins}-?",
                    teamPowerRating = prediction.TeamPowerRating,
                    opponentPowerRating = prediction.OpponentPowerRating,
                    rivalryNote = prediction.RivalryNote,
                    summary = prediction.PredictionSummary
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
        /// POST body example:
        /// {
        ///   "year": 2025,
        ///   "matchups": [
        ///     { "teamName": "Ohio State", "opponentName": "Michigan", "location": "H", "week": 12 },
        ///     { "teamName": "Alabama", "opponentName": "Auburn", "location": "N", "week": 13 }
        ///   ]
        /// }
        /// </summary>
        [HttpPost("predictMatchups")]
        public async Task<IActionResult> PredictMatchups([FromBody] MatchupBatchRequest request)
        {
            try
            {
                var predictions = await _predictionService.PredictMatchups(
                    request.Year, request.Matchups);

                return Ok(new
                {
                    message = $"Predicted {predictions.Count} matchups for {request.Year}",
                    predictions = predictions.Select(p => new
                    {
                        matchup = $"{p.TeamName} {p.LocationDisplay} {p.OpponentName}",
                        prediction = $"{p.TeamName} {p.PredictedTeamScore:F1}, {p.OpponentName} {p.PredictedOpponentScore:F1}",
                        expectedMargin = p.ExpectedMargin,
                        marginOfError = p.MarginOfError,
                        confidence = p.Confidence,
                        rivalryNote = p.RivalryNote,
                        summary = p.PredictionSummary
                    })
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error predicting matchups");
                return StatusCode(500, ex.Message);
            }
        }

        /// <summary>
        /// Diagnostic endpoint to check database data availability
        /// GET /api/productiongamedata/diagnostic
        /// </summary>
        [HttpGet("diagnostic")]
        public async Task<IActionResult> GetDiagnostic()
        {
            try
            {
                await using var context = await contextFactory.CreateDbContextAsync();

                var totalTeams = await context.Teams.CountAsync();
                var totalGames = await context.Games.CountAsync();
                var totalRecords = await context.TeamRecords.CountAsync();
                var recordsWithPowerRating = await context.TeamRecords.CountAsync(tr => tr.PowerRating.HasValue);

                var years = await context.TeamRecords
                    .Where(tr => tr.PowerRating.HasValue)
                    .Select(tr => tr.Year)
                    .Distinct()
                    .OrderBy(y => y)
                    .ToListAsync();

                var yearStats = new List<object>();
                foreach (var year in years)
                {
                    var count = await context.TeamRecords
                        .CountAsync(tr => tr.Year == year && tr.PowerRating.HasValue);
                    yearStats.Add(new { year, teamsWithRankings = count });
                }

                return Ok(new
                {
                    database = "Connected",
                    totalTeams,
                    totalGames,
                    totalRecords,
                    recordsWithPowerRating,
                    yearsWithData = years,
                    yearStats
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting diagnostic info");
                return StatusCode(500, ex.Message);
            }
        }

        /// <summary>
        /// Query team records with filters for wins, losses, year range, and PowerRating range.
        /// Example: GET /api/productiongamedata/queryTeamRecords?wins=13&losses=3
        /// Example: GET /api/productiongamedata/queryTeamRecords?minPowerRating=-0.02&maxPowerRating=0.01
        /// Example: GET /api/productiongamedata/queryTeamRecords?startYear=2020&endYear=2024&minWins=10
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
            [FromQuery] int limit = 50)
        {
            try
            {
                await using var context = await contextFactory.CreateDbContextAsync();

                var query = context.TeamRecords
                    .Include(tr => tr.Team)
                    .Where(tr => tr.PowerRating != null);

                // Apply filters
                if (wins.HasValue)
                    query = query.Where(tr => tr.Wins == wins.Value);

                if (losses.HasValue)
                    query = query.Where(tr => tr.Losses == losses.Value);

                if (minWins.HasValue)
                    query = query.Where(tr => tr.Wins >= minWins.Value);

                if (maxWins.HasValue)
                    query = query.Where(tr => tr.Wins <= maxWins.Value);

                if (startYear.HasValue)
                    query = query.Where(tr => tr.Year >= startYear.Value);

                if (endYear.HasValue)
                    query = query.Where(tr => tr.Year <= endYear.Value);

                if (minPowerRating.HasValue)
                    query = query.Where(tr => tr.PowerRating >= minPowerRating.Value);

                if (maxPowerRating.HasValue)
                    query = query.Where(tr => tr.PowerRating <= maxPowerRating.Value);

                var results = await query
                    .OrderByDescending(tr => tr.Year)
                    .ThenByDescending(tr => tr.PowerRating)
                    .Take(limit)
                    .Select(tr => new
                    {
                        tr.Year,
                        TeamName = tr.Team!.TeamName,
                        Record = $"{tr.Wins}-{tr.Losses}",
                        tr.Wins,
                        tr.Losses,
                        tr.PointsFor,
                        tr.PointsAgainst,
                        PointDifferential = tr.PointsFor - tr.PointsAgainst,
                        tr.BaseSOS,
                        tr.SubSOS,
                        tr.CombinedSOS,
                        tr.PowerRating
                    })
                    .ToListAsync();

                return Ok(new
                {
                    count = results.Count,
                    filters = new
                    {
                        wins,
                        losses,
                        minWins,
                        maxWins,
                        startYear,
                        endYear,
                        minPowerRating,
                        maxPowerRating,
                        limit
                    },
                    results
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error querying team records");
                return StatusCode(500, "An error occurred while querying team records.");
            }
        }

        /// <summary>
        /// Query matchup histories and detected rivalries.
        /// Omit all parameters or pass tier=ALL to get all matchups.
        /// Example: GET /api/productiongamedata/rivalries?tier=EPIC&minGames=50
        /// Example: GET /api/productiongamedata/rivalries (returns all)
        /// </summary>
        [HttpGet("rivalries")]
        public async Task<IActionResult> GetRivalries(
            [FromQuery] string? tier,
            [FromQuery] int? minGames,
            [FromQuery] double? minVarianceRatio)
        {
            try
            {
                await using var context = await contextFactory.CreateDbContextAsync();

                var query = context.MatchupHistories.AsQueryable();

                // Filter by tier if specified (and not "ALL")
                if (!string.IsNullOrEmpty(tier) && !tier.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(m => m.RivalryTier == tier);
                }

                // Filter by minimum games
                if (minGames.HasValue)
                {
                    query = query.Where(m => m.GamesPlayed >= minGames.Value);
                }

                var matchups = await query
                    .OrderByDescending(m => m.GamesPlayed)
                    .ToListAsync();

                logger.LogInformation("Found {Count} matchups matching filters", matchups.Count);

                // Get team names lookup in batch
                var teamIds = matchups.SelectMany(m => new[] { m.Team1Id, m.Team2Id }).Distinct().ToList();
                var teamNames = await context.Teams
                    .Where(t => teamIds.Contains(t.TeamID))
                    .ToDictionaryAsync(t => t.TeamID, t => t.TeamName);

                // Calculate average StDev once
                var avgScoreDeltas = await context.AvgScoreDeltas.ToListAsync();
                var avgStDev = avgScoreDeltas.Any() ? avgScoreDeltas.Average(a => (double)a.StDevP) : 15.0;

                // Build results
                var results = new List<object>();

                foreach (var matchup in matchups)
                {
                    var team1 = teamNames.GetValueOrDefault(matchup.Team1Id, "Unknown");
                    var team2 = teamNames.GetValueOrDefault(matchup.Team2Id, "Unknown");

                    // Calculate variance ratio
                    var varianceRatio = (double)matchup.StDevMargin / avgStDev;

                    // Apply minimum variance ratio filter if specified
                    if (minVarianceRatio.HasValue && varianceRatio < minVarianceRatio.Value)
                    {
                        continue;
                    }

                    results.Add(new
                    {
                        team1,
                        team2,
                        rivalryName = matchup.RivalryName ?? "N/A",
                        tier = matchup.RivalryTier ?? "N/A",
                        gamesPlayed = matchup.GamesPlayed,
                        avgMargin = Math.Round((double)matchup.AvgMargin, 1),
                        stDevMargin = Math.Round((double)matchup.StDevMargin, 1),
                        upsetRate = Math.Round((double)matchup.UpsetRate, 3),
                        varianceRatio = Math.Round(varianceRatio, 2),
                        seriesAge = matchup.LastPlayed - matchup.FirstPlayed,
                        firstPlayed = matchup.FirstPlayed,
                        lastPlayed = matchup.LastPlayed
                    });
                }

                return Ok(new
                {
                    totalMatchups = results.Count,
                    totalInDatabase = matchups.Count,
                    filters = new
                    {
                        tier = tier ?? "ALL",
                        minGames = minGames ?? 0,
                        minVarianceRatio = minVarianceRatio ?? 0.0
                    },
                    rivalries = results
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error querying rivalries");
                return StatusCode(500, "An error occurred while querying rivalries.");
            }
        }

        /// <summary>
        /// Get power rankings for a specific year.
        /// Example: GET /api/productiongamedata/powerrankings?year=2025
        /// </summary>
        [HttpGet("powerrankings")]
        public async Task<IActionResult> GetPowerRankings([FromQuery] int? year)
        {
            try
            {
                var targetYear = year ?? DateTime.Now.Year;

                await using var context = await contextFactory.CreateDbContextAsync();

                // Load data first, then order in memory (SQLite doesn't support ordering by decimal)
                var teamRecords = await context.TeamRecords
                    .Include(tr => tr.Team)
                    .Where(tr => tr.Year == targetYear && tr.Ranking.HasValue)
                    .ToListAsync();

                // Add conference tier to each team
                var teamsWithTiers = teamRecords
                    .Select(tr => new
                    {
                        TeamRecord = tr,
                        Tier = GetConferenceTier(tr.Team?.Conference, tr.Team?.TeamName)
                    })
                    .OrderByDescending(t => t.TeamRecord.Ranking)
                    .ToList();

                // Calculate overall rank (all teams)
                var withOverallRank = teamsWithTiers
                    .Select((t, index) => new
                    {
                        t.TeamRecord,
                        t.Tier,
                        OverallRank = index + 1
                    })
                    .ToList();

                // Group by tier and calculate tier-specific ranks
                var tierGroups = withOverallRank.GroupBy(t => t.Tier).ToList();

                // Dictionary to hold tier ranks
                var tierRankLookup = new Dictionary<int, int>(); // TeamID -> TierRank

                foreach (var tierGroup in tierGroups)
                {
                    var tieredTeams = tierGroup
                        .OrderByDescending(t => t.TeamRecord.Ranking)
                        .Select((t, index) => new { t.TeamRecord.TeamID, TierRank = index + 1 })
                        .ToList();

                    foreach (var team in tieredTeams)
                    {
                        tierRankLookup[team.TeamID] = team.TierRank;
                    }
                }

                // Build final rankings list
                var rankings = withOverallRank
                    .OrderByDescending(t => t.TeamRecord.Ranking)
                    .Select(t => new
                    {
                        TeamID = t.TeamRecord.TeamID,
                        TeamName = t.TeamRecord.Team!.TeamName,
                        Conference = t.TeamRecord.Team.Conference,
                        ConferenceAbbr = t.TeamRecord.Team.ConferenceAbbr,
                        Division = t.TeamRecord.Team.Division,
                        Tier = t.Tier,
                        OverallRank = t.OverallRank,                        // Rank among ALL teams
                        TierRank = tierRankLookup[t.TeamRecord.TeamID],    // Rank within tier (P4, G5, etc.)
                        Ranking = t.TeamRecord.Ranking,
                        Year = t.TeamRecord.Year,
                        Wins = t.TeamRecord.Wins,
                        Losses = t.TeamRecord.Losses,
                        BaseSOS = t.TeamRecord.BaseSOS,
                        CombinedSOS = t.TeamRecord.CombinedSOS
                    })
                    .ToList();

                logger.LogInformation("Found {Count} teams with power ratings for year {Year}", rankings.Count, targetYear);

                return Ok(rankings);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving power rankings");
                return StatusCode(500, "An error occurred while retrieving power rankings.");
            }
        }

        /// <summary>
        /// Get the full schedule for a season with actual and projected scores/O-U.
        /// Example: GET /api/productiongamedata/schedule?year=2025
        /// </summary>
        [HttpGet("schedule")]
        public async Task<IActionResult> GetSchedule([FromQuery] int? year)
        {
            try
            {
                var targetYear = year ?? DateTime.Now.Year;

                await using var context = await contextFactory.CreateDbContextAsync();

                // Load all games for the year
                var games = await context.Games
                    .Where(g => g.Year == targetYear)
                    .OrderBy(g => g.Week)
                    .ToListAsync();

                if (games.Count == 0)
                    return Ok(new List<object>());

                // Load team metadata keyed by TeamID for conference/tier lookups
                var teamIds = games.SelectMany(g => new[] { g.WinnerId, g.LoserId }).Distinct().ToList();
                var teams = await context.Teams
                    .Where(t => teamIds.Contains(t.TeamID))
                    .ToDictionaryAsync(t => t.TeamID);

                // Pre-game TeamRecords: for projection we use season-to-date stats
                // accumulated BEFORE each game (i.e., from the prior year for week 1,
                // or running totals from the same season up to but not including that week).
                // We approximate by loading the full-season record and using the existing
                // prediction service which is designed for pre-game use.
                var teamRecords = await context.TeamRecords
                    .Where(tr => tr.Year == targetYear)
                    .ToDictionaryAsync(tr => tr.TeamID);

                // Also load prior-year records as fallback for early-season games
                var priorYearRecords = await context.TeamRecords
                    .Where(tr => tr.Year == targetYear - 1)
                    .ToDictionaryAsync(tr => tr.TeamID);

                var avgScoreDeltas = await context.AvgScoreDeltas.ToListAsync();

                // Fallback average score if team records are unavailable
                const double fallbackScore = 28.0;

                var results = games.Select(g =>
                {
                    teams.TryGetValue(g.WinnerId, out var winner);
                    teams.TryGetValue(g.LoserId, out var loser);

                    var winnerConf = winner?.ConferenceAbbr ?? winner?.Conference ?? "";
                    var loserConf = loser?.ConferenceAbbr ?? loser?.Conference ?? "";
                    var winnerTier = GetConferenceTier(winner?.Conference, winner?.TeamName);
                    var loserTier = GetConferenceTier(loser?.Conference, loser?.TeamName);

                    // Projected scores: use team-specific PPG/PAG baselines so each
                    // matchup reflects the actual offensive/defensive profiles of both teams.
                    double? projWinner = null, projLoser = null;
                    try
                    {
                        var wrec = teamRecords.GetValueOrDefault(g.WinnerId)
                                   ?? priorYearRecords.GetValueOrDefault(g.WinnerId);
                        var lrec = teamRecords.GetValueOrDefault(g.LoserId)
                                   ?? priorYearRecords.GetValueOrDefault(g.LoserId);

                        if (wrec != null && lrec != null)
                        {
                            var wGames = wrec.Wins + wrec.Losses;
                            var lGames = lrec.Wins + lrec.Losses;

                            // Team-specific scoring baselines (PPG / PAG)
                            var wPPG = wGames > 0 ? wrec.PointsFor / (double)wGames : fallbackScore;
                            var wPAG = wGames > 0 ? wrec.PointsAgainst / (double)wGames : fallbackScore;
                            var lPPG = lGames > 0 ? lrec.PointsFor / (double)lGames : fallbackScore;
                            var lPAG = lGames > 0 ? lrec.PointsAgainst / (double)lGames : fallbackScore;

                            // Blend each team's offense against opponent's defense
                            var wBaseScore = (wPPG + lPAG) / 2.0;
                            var lBaseScore = (lPPG + wPAG) / 2.0;

                            // Win-pct delta lookup
                            var wWinPct = wGames > 0
                                ? Math.Round((decimal)wrec.Wins / wGames * 20m, MidpointRounding.AwayFromZero) / 20m
                                : 0m;
                            var lWinPct = lGames > 0
                                ? Math.Round((decimal)lrec.Wins / lGames * 20m, MidpointRounding.AwayFromZero) / 20m
                                : 0m;

                            var maxPct = Math.Max(wWinPct, lWinPct);
                            var minPct = Math.Min(wWinPct, lWinPct);
                            var asd = avgScoreDeltas.FirstOrDefault(
                                a => a.Team1WinPct == maxPct && a.Team2WinPct == minPct);
                            var delta = asd != null && asd.SampleSize >= 10
                                ? Math.Max(-35.0, Math.Min(35.0, (double)asd.AverageScoreDelta))
                                : 7.0;

                            // Delta from winner's perspective
                            var deltaFromWinner = wWinPct >= lWinPct ? delta : -delta;

                            // Home field advantage: 'W' = winner is home, 'L' = loser is home
                            var hfa = g.Location == 'W' ? 2.5 : g.Location == 'L' ? -2.5 : 0.0;
                            deltaFromWinner += hfa;

                            // Power rating adjustment
                            if (wrec.PowerRating.HasValue && lrec.PowerRating.HasValue)
                            {
                                var prDiff = (double)(wrec.PowerRating.Value - lrec.PowerRating.Value);
                                deltaFromWinner += prDiff * 10.0;
                            }

                            projWinner = Math.Max(0, Math.Round(wBaseScore + deltaFromWinner / 2.0, 1));
                            projLoser = Math.Max(0, Math.Round(lBaseScore - deltaFromWinner / 2.0, 1));
                        }
                    }
                    catch { /* projection unavailable */ }

                    var actualOU = g.WPoints + g.LPoints;
                    var projOU = projWinner.HasValue && projLoser.HasValue
                                   ? Math.Round(projWinner.Value + projLoser.Value, 1)
                                   : (double?)null;

                    return new
                    {
                        g.Id,
                        g.Year,
                        g.Week,
                        GameDate = g.GameDate,
                        GameDay = g.GameDay,
                        WinnerName = g.WinnerName,
                        WinnerShortName = winner?.ShortName ?? g.WinnerName,
                        WinnerId = g.WinnerId,
                        WinnerConf = winnerConf,
                        WinnerTier = winnerTier,
                        WPoints = g.WPoints,
                        LoserName = g.LoserName,
                        LoserShortName = loser?.ShortName ?? g.LoserName,
                        LoserId = g.LoserId,
                        LoserConf = loserConf,
                        LoserTier = loserTier,
                        LPoints = g.LPoints,
                        g.Location,
                        ActualOU = actualOU,
                        ProjWinnerScore = projWinner,
                        ProjLoserScore = projLoser,
                        ProjOU = projOU
                    };
                }).ToList();

                return Ok(results);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving schedule");
                return StatusCode(500, "An error occurred while retrieving the schedule.");
            }
        }

        /// <summary>
        /// Returns all FBS teams with id, name, short name, conference, and tier.
        /// Example: GET /api/productiongamedata/teams
        /// </summary>
        [HttpGet("teams")]
        public async Task<IActionResult> GetTeams()
        {
            try
            {
                await using var context = await contextFactory.CreateDbContextAsync();

                var teams = await context.Teams
                    .OrderBy(t => t.TeamName)
                    .Select(t => new
                    {
                        t.TeamID,
                        t.TeamName,
                        ShortName      = t.ShortName ?? t.TeamName,
                        t.Conference,
                        ConferenceAbbr = t.ConferenceAbbr ?? "",
                        t.Division,
                        Tier           = GetConferenceTier(t.Conference, t.TeamName)
                    })
                    .ToListAsync();

                return Ok(teams);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving teams");
                return StatusCode(500, "An error occurred while retrieving teams.");
            }
        }

        /// <summary>
        /// Returns only named (whitelisted) rivalries with team names and series stats.
        /// Example: GET /api/productiongamedata/rivalries/named
        /// </summary>
        [HttpGet("rivalries/named")]
        public async Task<IActionResult> GetNamedRivalries()
        {
            try
            {
                await using var context = await contextFactory.CreateDbContextAsync();

                var rivalries = await context.MatchupHistories
                    .Where(m => m.RivalryName != null)
                    .OrderBy(m => m.RivalryTier)
                    .ThenBy(m => m.RivalryName)
                    .ToListAsync();

                var teamIds = rivalries.SelectMany(r => new[] { r.Team1Id, r.Team2Id }).Distinct().ToList();
                var teams = await context.Teams
                    .Where(t => teamIds.Contains(t.TeamID))
                    .ToDictionaryAsync(t => t.TeamID);

                var result = rivalries.Select(r =>
                {
                    teams.TryGetValue(r.Team1Id, out var t1);
                    teams.TryGetValue(r.Team2Id, out var t2);
                    return new
                    {
                        r.Team1Id,
                        Team1Name      = t1?.TeamName ?? "Unknown",
                        Team1ShortName = t1?.ShortName ?? t1?.TeamName ?? "Unknown",
                        r.Team2Id,
                        Team2Name      = t2?.TeamName ?? "Unknown",
                        Team2ShortName = t2?.ShortName ?? t2?.TeamName ?? "Unknown",
                        r.RivalryName,
                        r.RivalryTier,
                        r.GamesPlayed,
                        r.AvgMargin,
                        r.StDevMargin,
                        r.UpsetRate,
                        r.FirstPlayed,
                        r.LastPlayed
                    };
                }).ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving named rivalries");
                return StatusCode(500, "An error occurred while retrieving named rivalries.");
            }
        }

        /// <summary>
        /// Returns yearly rank/rating/SOS history for a team (up to N years back).
        /// Example: GET /api/productiongamedata/teamhistory?teamId=47&years=10
        /// </summary>
        [HttpGet("teamhistory")]
        public async Task<IActionResult> GetTeamHistory([FromQuery] int teamId, [FromQuery] int years = 10)
        {
            try
            {
                await using var context = await contextFactory.CreateDbContextAsync();

                var team = await context.Teams.FindAsync(teamId);
                if (team == null)
                    return NotFound($"Team {teamId} not found.");

                var cutoffYear = (short)(DateTime.Now.Year - years);

                var records = await context.TeamRecords
                    .Where(tr => tr.TeamID == teamId && tr.Year >= cutoffYear)
                    .OrderBy(tr => tr.Year)
                    .ToListAsync();

                // Compute overall rank per year by ordering all teams by rating
                var allYears = records.Select(r => r.Year).Distinct().ToList();
                var ranksByYear = new Dictionary<short, int>();

                foreach (var yr in allYears)
                {
                    var allRanked = (await context.TeamRecords
                        .Where(tr => tr.Year == yr && tr.Ranking.HasValue)
                        .ToListAsync())
                        .OrderByDescending(tr => tr.Ranking)
                        .ToList();

                    var idx = allRanked.FindIndex(tr => tr.TeamID == teamId);
                    if (idx >= 0) ranksByYear[yr] = idx + 1;
                }

                var result = records.Select(r => new
                {
                    Year          = (int)r.Year,
                    r.Wins,
                    r.Losses,
                    Record        = $"{r.Wins}-{r.Losses}",
                    PowerRating   = r.Ranking,
                    BaseSOS       = r.BaseSOS,
                    CombinedSOS   = r.CombinedSOS,
                    OverallRank   = ranksByYear.GetValueOrDefault(r.Year, 0),
                    Tier          = GetConferenceTier(team.Conference, team.TeamName)
                }).ToList();

                return Ok(new
                {
                    TeamId        = teamId,
                    TeamName      = team.TeamName,
                    ShortName     = team.ShortName ?? team.TeamName,
                    ConferenceAbbr = team.ConferenceAbbr,
                    History       = result
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving team history for {TeamId}", teamId);
                return StatusCode(500, "An error occurred while retrieving team history.");
            }
        }

        /// <summary>
        /// Returns yearly game results for a rivalry matchup (up to N years back).
        /// Also includes the projected result for the current/upcoming year if data exists.
        /// Example: GET /api/productiongamedata/rivalryhistory?team1Id=12&team2Id=47&years=10
        /// </summary>
        [HttpGet("rivalryhistory")]
        public async Task<IActionResult> GetRivalryHistory(
            [FromQuery] int team1Id,
            [FromQuery] int team2Id,
            [FromQuery] int years = 10)
        {
            try
            {
                await using var context = await contextFactory.CreateDbContextAsync();

                var team1 = await context.Teams.FindAsync(team1Id);
                var team2 = await context.Teams.FindAsync(team2Id);
                if (team1 == null || team2 == null)
                    return NotFound("One or both teams not found.");

                var cutoffYear = DateTime.Now.Year - years;

                // Games where either team was winner or loser
                var games = await context.Games
                    .Where(g => g.Year >= cutoffYear &&
                        ((g.WinnerId == team1Id && g.LoserId == team2Id) ||
                         (g.WinnerId == team2Id && g.LoserId == team1Id)))
                    .OrderBy(g => g.Year)
                    .ThenBy(g => g.Week)
                    .ToListAsync();

                // Rivalry metadata
                var rivalry = await context.MatchupHistories
                    .FirstOrDefaultAsync(m =>
                        (m.Team1Id == team1Id && m.Team2Id == team2Id) ||
                        (m.Team1Id == team2Id && m.Team2Id == team1Id));

                // Load season records for projection
                var avgScoreDeltas = await context.AvgScoreDeltas.ToListAsync();
                var avgTeamScore   = games.Count > 0
                    ? (games.Average(g => g.WPoints) + games.Average(g => g.LPoints)) / 2.0
                    : 28.0;

                var history = games.Select(g =>
                {
                    // From team1's perspective
                    var team1Won   = g.WinnerId == team1Id;
                    var team1Score = team1Won ? g.WPoints : g.LPoints;
                    var team2Score = team1Won ? g.LPoints : g.WPoints;

                    return new
                    {
                        g.Year,
                        g.Week,
                        g.Location,
                        Team1Score   = team1Score,
                        Team2Score   = team2Score,
                        Margin       = team1Score - team2Score,   // positive = team1 won
                        ActualOU     = g.WPoints + g.LPoints,
                        Team1Won     = team1Won,
                        WinnerName   = team1Won ? team1.TeamName : team2.TeamName,
                        Score        = $"{g.WPoints}-{g.LPoints}"
                    };
                }).ToList();

                // Project current year if both teams have records
                object? projection = null;
                var currentYear = (short)DateTime.Now.Year;
                var t1Record = await context.TeamRecords
                    .FirstOrDefaultAsync(tr => tr.TeamID == team1Id && tr.Year == currentYear);
                var t2Record = await context.TeamRecords
                    .FirstOrDefaultAsync(tr => tr.TeamID == team2Id && tr.Year == currentYear);

                if (t1Record != null && t2Record != null)
                {
                    var t1Games  = t1Record.Wins + t1Record.Losses;
                    var t2Games  = t2Record.Wins + t2Record.Losses;
                    var t1WinPct = t1Games > 0 ? Math.Round((decimal)t1Record.Wins / t1Games * 20m, MidpointRounding.AwayFromZero) / 20m : 0m;
                    var t2WinPct = t2Games > 0 ? Math.Round((decimal)t2Record.Wins / t2Games * 20m, MidpointRounding.AwayFromZero) / 20m : 0m;
                    var maxPct   = Math.Max(t1WinPct, t2WinPct);
                    var minPct   = Math.Min(t1WinPct, t2WinPct);
                    var asd      = avgScoreDeltas.FirstOrDefault(a => a.Team1WinPct == maxPct && a.Team2WinPct == minPct);
                    var delta    = asd != null && asd.SampleSize >= 10
                        ? Math.Max(-35.0, Math.Min(35.0, (double)asd.AverageScoreDelta))
                        : 7.0;
                    var deltaFromT1 = t1WinPct >= t2WinPct ? delta : -delta;
                    if (t1Record.Ranking.HasValue && t2Record.Ranking.HasValue)
                        deltaFromT1 += (double)(t1Record.Ranking.Value - t2Record.Ranking.Value) * 0.15;

                    var projT1 = Math.Round(avgTeamScore + deltaFromT1 / 2.0, 1);
                    var projT2 = Math.Round(avgTeamScore - deltaFromT1 / 2.0, 1);

                    projection = new
                    {
                        Year         = currentYear,
                        ProjTeam1Score = projT1,
                        ProjTeam2Score = projT2,
                        ProjMargin   = Math.Round(projT1 - projT2, 1),
                        ProjOU       = Math.Round(projT1 + projT2, 1),
                        IsProjected  = true
                    };
                }

                return Ok(new
                {
                    Team1Id        = team1Id,
                    Team1Name      = team1.TeamName,
                    Team1ShortName = team1.ShortName ?? team1.TeamName,
                    Team2Id        = team2Id,
                    Team2Name      = team2.TeamName,
                    Team2ShortName = team2.ShortName ?? team2.TeamName,
                    RivalryName    = rivalry?.RivalryName,
                    RivalryTier    = rivalry?.RivalryTier,
                    GamesPlayed    = rivalry?.GamesPlayed ?? history.Count,
                    AvgMargin      = rivalry?.AvgMargin,
                    UpsetRate      = rivalry?.UpsetRate,
                    History        = history,
                    CurrentYearProjection = projection
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving rivalry history for {T1} vs {T2}", team1Id, team2Id);
                return StatusCode(500, "An error occurred while retrieving rivalry history.");
            }
        }

        /// <summary>
        /// Returns the projected conference championship game qualifiers for all
        /// FBS conferences based on current season standings.
        /// Example: GET /api/productiongamedata/championship-qualifiers?year=2025
        /// </summary>
        [HttpGet("championship-qualifiers")]
        public async Task<IActionResult> GetChampionshipQualifiers([FromQuery] int? year)
        {
            try
            {
                var targetYear = year ?? DateTime.Now.Year;
                await using var context = await contextFactory.CreateDbContextAsync();

                // Build fully-populated standings from Games table
                var standingsByConference = await BuildConferenceStandings(context, targetYear);

                var service = new ConferenceChampionshipService();

                var results = standingsByConference
                    .Where(kvp => kvp.Value.Count >= 2)
                    .Select(kvp => service.GetQualifiers(kvp.Key, kvp.Value))
                    .Where(r => r.Qualifier1 != null && r.Qualifier2 != null)
                    .OrderBy(r => r.Conference switch
                    {
                        "SEC" => 1,
                        "Big Ten" => 2,
                        "ACC" => 3,
                        "Big 12" => 4,
                        "AAC" => 5,
                        "MW" => 6,
                        "MAC" => 7,
                        "C-USA" => 8,
                        "Sun Belt" => 9,
                        _ => 99   // anything else goes to the bottom
                    })
                    .Select(r => new
                    {
                        r.Conference,
                        r.Format,
                        Qualifier1 = new
                        {
                            r.Qualifier1.TeamName,
                            r.Qualifier1.ConferenceWins,
                            r.Qualifier1.ConferenceLosses,
                            r.Qualifier1.OverallWins,
                            r.Qualifier1.OverallLosses,
                            r.Qualifier1.Division
                        },
                        Qualifier2 = new
                        {
                            r.Qualifier2.TeamName,
                            r.Qualifier2.ConferenceWins,
                            r.Qualifier2.ConferenceLosses,
                            r.Qualifier2.OverallWins,
                            r.Qualifier2.OverallLosses,
                            r.Qualifier2.Division
                        },
                        r.Qualifier1Method,
                        r.Qualifier2Method,
                        r.TiebreakerLog,
                        r.StubsApplied
                    })
                    .ToList();

                return Ok(results);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error computing championship qualifiers");
                return StatusCode(500, "An error occurred computing championship qualifiers.");
            }
        }

        /// <summary>
        /// Returns projected conference championship game qualifiers by combining
        /// actual results through the specified week with GamePredictionService
        /// projections for all remaining unplayed games.
        ///
        /// throughWeek parameter: simulate mid-season by treating games after
        /// this week as unplayed. Omit to use all actual results to date.
        ///
        /// Examples:
        ///   GET /api/productiongamedata/projected-championship-qualifiers?year=2025
        ///   GET /api/productiongamedata/projected-championship-qualifiers?year=2025&throughWeek=8
        /// </summary>
        [HttpGet("projected-championship-qualifiers")]
        public async Task<IActionResult> GetProjectedChampionshipQualifiers(
            [FromQuery] int? year,
            [FromQuery] int? throughWeek,
            CancellationToken token = default)
        {
            try
            {
                var targetYear = year ?? DateTime.Now.Year;
                await using var context = await contextFactory.CreateDbContextAsync(token);

                var standingsByConference = await BuildProjectedConferenceStandings(
                    context, targetYear, throughWeek, token);

                var service = new ConferenceChampionshipService();

                var results = standingsByConference
                    .Where(kvp => kvp.Value.Count >= 2)
                    .Select(kvp => service.GetQualifiers(kvp.Key, kvp.Value))
                    .Where(r => r.Qualifier1 != null && r.Qualifier2 != null)
                    .OrderBy(r => r.Conference switch
                    {
                        "SEC" => 1,
                        "Big Ten" => 2,
                        "ACC" => 3,
                        "Big 12" => 4,
                        "AAC" => 5,
                        "MW" => 6,
                        "MAC" => 7,
                        "C-USA" => 8,
                        "Sun Belt" => 9,
                        _ => 99
                    })
                    .Select(r => new
                    {
                        r.Conference,
                        r.Format,
                        Qualifier1 = new
                        {
                            r.Qualifier1.TeamName,
                            r.Qualifier1.ConferenceWins,
                            r.Qualifier1.ConferenceLosses,
                            r.Qualifier1.OverallWins,
                            r.Qualifier1.OverallLosses,
                            r.Qualifier1.Division
                        },
                        Qualifier2 = new
                        {
                            r.Qualifier2.TeamName,
                            r.Qualifier2.ConferenceWins,
                            r.Qualifier2.ConferenceLosses,
                            r.Qualifier2.OverallWins,
                            r.Qualifier2.OverallLosses,
                            r.Qualifier2.Division
                        },
                        r.Qualifier1Method,
                        r.Qualifier2Method,
                        r.TiebreakerLog,
                        r.StubsApplied,
                        SimulatedThrough = throughWeek.HasValue
                            ? $"Week {throughWeek} (weeks {throughWeek + 1}-15 projected)"
                            : "Full season actual results"
                    })
                    .ToList();

                return Ok(results);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error computing projected championship qualifiers");
                return StatusCode(500, "An error occurred computing projected championship qualifiers.");
            }
        }

        /// <summary>
        /// Returns projected final conference standings for all FBS teams,
        /// showing each team's actual results to date and projected results
        /// for remaining games, with per-game detail.
        ///
        /// throughWeek: simulate mid-season by treating games after this week
        /// as unplayed. Omit to use all actual results to date.
        ///
        /// conference: optional filter to a single conference abbreviation.
        ///
        /// Examples:
        ///   GET /api/productiongamedata/projected-standings?year=2025
        ///   GET /api/productiongamedata/projected-standings?year=2025&throughWeek=8
        ///   GET /api/productiongamedata/projected-standings?year=2025&throughWeek=8&conference=SEC
        /// </summary>
        [HttpGet("projected-standings")]
        public async Task<IActionResult> GetProjectedStandings(
            [FromQuery] int? year,
            [FromQuery] int? throughWeek,
            [FromQuery] string conference = null,
            CancellationToken token = default)
        {
            try
            {
                var targetYear = year ?? DateTime.Now.Year;
                await using var context = await contextFactory.CreateDbContextAsync(token);

                var teams = await context.Teams.ToDictionaryAsync(t => t.TeamID, token);
                var records = await context.TeamRecords
                    .Where(tr => tr.Year == targetYear)
                    .ToDictionaryAsync(tr => tr.TeamID, token);

                // FBS team IDs
                var fbsTeamIds = teams.Values
                    .Where(t => t.Division == "FBS")
                    .Select(t => t.TeamID)
                    .ToHashSet();

                // All regular season games
                var allGames = await context.Games
                    .Where(g => g.Year == targetYear && g.Week < 16)
                    .ToListAsync(token);

                // Conference game predicate
                bool IsConfGame(Game g) =>
                    fbsTeamIds.Contains(g.WinnerId) &&
                    fbsTeamIds.Contains(g.LoserId) &&
                    teams.TryGetValue(g.WinnerId, out var w) &&
                    teams.TryGetValue(g.LoserId, out var l) &&
                    !string.IsNullOrEmpty(w.ConferenceAbbr) &&
                    w.ConferenceAbbr == l.ConferenceAbbr;

                // Split played vs unplayed
                var playedConfGames = allGames
                    .Where(g => IsConfGame(g) &&
                                (g.WPoints > 0 || g.LPoints > 0) &&
                                (!throughWeek.HasValue || g.Week <= throughWeek.Value))
                    .ToList();

                var unplayedConfGames = allGames
                    .Where(g => IsConfGame(g) &&
                                (g.WPoints == 0 && g.LPoints == 0 ||
                                 throughWeek.HasValue && g.Week > throughWeek.Value))
                    .ToList();

                // ── Project unplayed games ────────────────────────────────────────
                var matchupRequests = unplayedConfGames
                    .Where(g => teams.ContainsKey(g.WinnerId) && teams.ContainsKey(g.LoserId))
                    .Select(g => new MatchupRequest
                    {
                        TeamName = teams[g.WinnerId].TeamName,
                        OpponentName = teams[g.LoserId].TeamName,
                        Location = g.Location,
                        Week = g.Week
                    })
                    .ToList();

                var predictions = matchupRequests.Any()
                    ? await predictionService.PredictMatchups(targetYear, matchupRequests, token)
                    : new List<GamePrediction>();

                // Key: "TeamName|OpponentName" → prediction
                var predictionMap = predictions.ToDictionary(
                    p => $"{p.TeamName}|{p.OpponentName}",
                    p => p
                );

                // ── Build per-team projected schedule ─────────────────────────────
                var targetTeams = teams.Values
                    .Where(t => t.Division == "FBS" &&
                                !string.IsNullOrEmpty(t.ConferenceAbbr) &&
                                t.ConferenceAbbr != "IND" &&
                                t.ConferenceAbbr != "Pac-12" &&
                                (conference == null ||
                                 t.ConferenceAbbr.Equals(conference, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                var teamResults = targetTeams.Select(team =>
                {
                    // All conference games involving this team
                    var teamConfGames = allGames
                        .Where(g => IsConfGame(g) &&
                                    (g.WinnerId == team.TeamID || g.LoserId == team.TeamID) &&
                                    g.Week < 16)
                        .OrderBy(g => g.Week)
                        .ToList();

                    int actualWins = 0, actualLosses = 0;
                    int projWins = 0, projLosses = 0;

                    var gameDetails = teamConfGames.Select(g =>
                    {
                        var isHome = g.WinnerId == team.TeamID
                            ? g.Location == 'W'
                            : g.Location == 'L';
                        var oppId = g.WinnerId == team.TeamID ? g.LoserId : g.WinnerId;
                        teams.TryGetValue(oppId, out var opp);
                        var oppName = opp?.ShortName ?? opp?.TeamName ?? "Unknown";

                        bool isPlayed = (g.WPoints > 0 || g.LPoints > 0) &&
                                         (!throughWeek.HasValue || g.Week <= throughWeek.Value);

                        if (isPlayed)
                        {
                            bool won = g.WinnerId == team.TeamID;
                            if (won) actualWins++; else actualLosses++;

                            var teamScore = won ? g.WPoints : g.LPoints;
                            var oppScore = won ? g.LPoints : g.WPoints;

                            return new
                            {
                                g.Week,
                                Opponent = oppName,
                                Location = isHome ? "vs" : "@",
                                Result = won ? "W" : "L",
                                Score = $"{teamScore}-{oppScore}",
                                ProjScore = (string)null,
                                Confidence = (string)null,
                                Type = "Actual",
                                NeutralSite = g.Location == 'N'
                            };
                        }
                        else
                        {
                            // Get projection
                            var teamName = team.TeamName;
                            var oppTeamName = opp?.TeamName ?? "Unknown";
                            var key = $"{teamName}|{oppTeamName}";
                            var altKey = $"{oppTeamName}|{teamName}";

                            double projTeamScore = 0, projOppScore = 0;
                            string confidence = "Unknown";
                            bool projWin = false;

                            if (predictionMap.TryGetValue(key, out var pred))
                            {
                                projTeamScore = pred.PredictedTeamScore;
                                projOppScore = pred.PredictedOpponentScore;
                                confidence = pred.Confidence;
                                projWin = projTeamScore >= projOppScore;
                            }
                            else if (predictionMap.TryGetValue(altKey, out var altPred))
                            {
                                projTeamScore = altPred.PredictedOpponentScore;
                                projOppScore = altPred.PredictedTeamScore;
                                confidence = altPred.Confidence;
                                projWin = projTeamScore >= projOppScore;
                            }
                            else
                            {
                                // No prediction — assume home team wins
                                projWin = isHome;
                            }

                            if (projWin) projWins++; else projLosses++;

                            return new
                            {
                                g.Week,
                                Opponent = oppName,
                                Location = isHome ? "vs" : "@",
                                Result = projWin ? "W" : "L",
                                Score = (string)null,
                                ProjScore = projTeamScore > 0
                                    ? $"{Math.Round(projTeamScore)}-{Math.Round(projOppScore)}"
                                    : null,
                                Confidence = confidence,
                                Type = "Projected",
                                NeutralSite = g.Location == 'N'
                            };
                        }
                    }).ToList();

                    // Projected finish rank within conference — computed after all teams processed
                    return new
                    {
                        team.TeamName,
                        Conference = team.ConferenceAbbr,
                        Division = GetDivision(team.TeamName, team.ConferenceAbbr),
                        ActualWins = actualWins,
                        ActualLosses = actualLosses,
                        ProjectedWins = actualWins + projWins,
                        ProjectedLosses = actualLosses + projLosses,
                        ProjectedWinPct = Math.Round(
                            (actualWins + projWins + actualLosses + projLosses) > 0
                                ? (double)(actualWins + projWins) /
                                  (actualWins + projWins + actualLosses + projLosses)
                                : 0.0, 3),
                        Games = gameDetails,
                        SimulatedThrough = throughWeek.HasValue
                            ? $"Week {throughWeek}"
                            : "Current"
                    };
                }).ToList();

                // ── Sort: by conference, then projected win pct desc ──────────────
                var sorted = teamResults
                    .OrderBy(t => t.Conference switch
                    {
                        "SEC" => 1,
                        "Big Ten" => 2,
                        "ACC" => 3,
                        "Big 12" => 4,
                        "AAC" => 5,
                        "MW" => 6,
                        "MAC" => 7,
                        "C-USA" => 8,
                        "Sun Belt" => 9,
                        _ => 99
                    })
                    .ThenByDescending(t => t.ProjectedWinPct)
                    .ThenByDescending(t => t.ProjectedWins)
                    .ToList();

                return Ok(sorted);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error computing projected standings");
                return StatusCode(500, "An error occurred computing projected standings.");
            }
        }


        /// <summary>
        /// Determines the conference tier for rankings.
        /// P4 = Power 4 conferences (SEC, Big Ten, Big 12, ACC)
        /// G5 = Group of 5 conferences (American, Mountain West, Sun Belt, MAC, C-USA)
        /// Independent = FBS independents (Army, Liberty, etc.)
        /// Team-name overrides handle edge cases where conference data doesn't reflect competitive tier.
        /// </summary>
        private static string GetConferenceTier(string? conference, string? teamName = null)
        {
            // Team-name overrides for independents whose tier doesn't match their conference string
            if (!string.IsNullOrEmpty(teamName))
            {
                if (teamName.Equals("Notre Dame", StringComparison.OrdinalIgnoreCase))
                    return "P4";
                if (teamName.Equals("Connecticut", StringComparison.OrdinalIgnoreCase))
                    return "G5";
            }

            if (string.IsNullOrEmpty(conference))
                return "Other";

            // Power 4 conferences
            var power4 = new[] { "SEC", "Big Ten", "Big 12", "ACC" };
            if (power4.Any(p4 => conference.Contains(p4, StringComparison.OrdinalIgnoreCase)))
                return "P4";

            // Group of 5 conferences
            var group5 = new[]
            {
                "American Athletic", "American", "AAC",
                "Mountain West",
                "Sun Belt",
                "Mid-American", "MAC",
                "Conference USA", "C-USA",
                "Pac-12"
            };
            if (group5.Any(g5 => conference.Contains(g5, StringComparison.OrdinalIgnoreCase)))
                return "G5";

            // Independent teams (Army, Liberty, etc.)
            if (conference.Contains("Independent", StringComparison.OrdinalIgnoreCase))
                return "Independent";

            return "Other";
        }

        /// <summary>
        /// Builds fully-populated ConferenceStanding objects for all teams in a
        /// given year by querying the Games table for conference-only matchups.
        /// </summary>
        private async Task<Dictionary<string, List<ConferenceStanding>>> BuildConferenceStandings(NCAAContext context,int year)
        {
            var teams = await context.Teams.ToDictionaryAsync(t => t.TeamID);
            var records = await context.TeamRecords
                .Where(tr => tr.Year == year)
                .ToDictionaryAsync(tr => tr.TeamID);

            // Pull FBS conference IDs
            var fbsTeamIds = teams.Values
                .Where(t => t.Division == "FBS")
                .Select(t => t.TeamID)
                .ToHashSet();

            // Load regular season conference games only (week <= 15)
            var allGames = await context.Games
                .Where(g => g.Year == year && g.Week <= 15)
                .ToListAsync();

            var confGames = allGames.Where(g =>
                fbsTeamIds.Contains(g.WinnerId) &&
                fbsTeamIds.Contains(g.LoserId) &&
                teams.TryGetValue(g.WinnerId, out var w) &&
                teams.TryGetValue(g.LoserId, out var l) &&
                w.ConferenceAbbr == l.ConferenceAbbr &&
                !string.IsNullOrEmpty(w.ConferenceAbbr)
            ).ToList();

            // ── CTE 1: ConfRecords — conference W/L per team ──────────────────
            var confRecords = teams.Values
                .Where(t => t.Division == "FBS" && !string.IsNullOrEmpty(t.ConferenceAbbr))
                .Select(t =>
                {
                    var confWins = confGames.Count(g => g.WinnerId == t.TeamID);
                    var confLosses = confGames.Count(g => g.LoserId == t.TeamID);
                    records.TryGetValue(t.TeamID, out var rec);

                    return new ConferenceStanding
                    {
                        TeamId = t.TeamID,
                        TeamName = t.TeamName,
                        Conference = t.ConferenceAbbr,
                        Division = GetDivision(t.TeamName, t.ConferenceAbbr),
                        ConferenceWins = confWins,
                        ConferenceLosses = confLosses,
                        OverallWins = rec != null ? (int)rec.Wins : 0,
                        OverallLosses = rec != null ? (int)rec.Losses : 0,
                        ConfPointsFor = confGames
                            .Where(g => g.WinnerId == t.TeamID).Sum(g => g.WPoints) +
                            confGames
                            .Where(g => g.LoserId == t.TeamID).Sum(g => g.LPoints),
                        ConfPointsAgainst = confGames
                            .Where(g => g.WinnerId == t.TeamID).Sum(g => g.LPoints) +
                            confGames
                            .Where(g => g.LoserId == t.TeamID).Sum(g => g.WPoints),
                    };
                })
                .ToList();

            // ── CTE 2: HeadToHeadResults — results vs conference opponents ────
            foreach (var standing in confRecords)
            {
                standing.HeadToHeadResults = confGames
                    .Where(g => g.WinnerId == standing.TeamId || g.LoserId == standing.TeamId)
                    .GroupBy(g => g.WinnerId == standing.TeamId ? g.LoserId : g.WinnerId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Count(game => game.WinnerId == standing.TeamId) >
                             g.Count(game => game.LoserId == standing.TeamId)
                    );
            }

            // ── CTE 3: CommonOpponentWinPct — win pct vs conf opponents ──────
            // (used as tiebreaker step 3 — refined to common opponents in engine)
            foreach (var standing in confRecords)
            {
                var oppIds = standing.HeadToHeadResults.Keys.ToList();
                var wins = standing.HeadToHeadResults.Values.Count(v => v);
                var total = standing.HeadToHeadResults.Count;
                standing.CommonOpponentWinPct = total > 0
                    ? (double)wins / total : 0.0;
            }

            // ── CTE 4: ConfSOS — avg conf win pct of all conf opponents ──────
            var recordById = confRecords.ToDictionary(r => r.TeamId);
            foreach (var standing in confRecords)
            {
                var oppWinPcts = standing.HeadToHeadResults.Keys
                    .Where(id => recordById.ContainsKey(id))
                    .Select(id => recordById[id].ConferenceWinPct)
                    .ToList();

                standing.ConferenceOpponentWinPct = oppWinPcts.Any()
                    ? oppWinPcts.Average() : 0.0;
            }

            // ── Group by conference ───────────────────────────────────────────
            return confRecords                
                .Where(s => s.Conference != "IND" &&
                        s.Conference != "Pac-12" &&
                        !string.IsNullOrEmpty(s.Conference))
                .GroupBy(s => s.Conference)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        /// <summary>
        /// Builds ConferenceStanding objects using:
        ///   - Actual results for games played on or before throughWeek
        ///   - GamePredictionService projections for all remaining games
        /// If throughWeek is null, all games with scores are treated as played.
        /// </summary>
        private async Task<Dictionary<string, List<ConferenceStanding>>> BuildProjectedConferenceStandings(
            NCAAContext context,
            int year,
            int? throughWeek = null,
            CancellationToken token = default)
        {
            var teams = await context.Teams.ToDictionaryAsync(t => t.TeamID, token);
            var records = await context.TeamRecords
                .Where(tr => tr.Year == year)
                .ToDictionaryAsync(tr => tr.TeamID, token);

            // FBS team IDs
            var fbsTeamIds = teams.Values
                .Where(t => t.Division == "FBS")
                .Select(t => t.TeamID)
                .ToHashSet();

            // All regular season games
            var allGames = await context.Games
                .Where(g => g.Year == year && g.Week < 16)
                .ToListAsync(token);

            // Conference game predicate
            bool IsConfGame(Game g) =>
                fbsTeamIds.Contains(g.WinnerId) &&
                fbsTeamIds.Contains(g.LoserId) &&
                teams.TryGetValue(g.WinnerId, out var w) &&
                teams.TryGetValue(g.LoserId, out var l) &&
                !string.IsNullOrEmpty(w.ConferenceAbbr) &&
                w.ConferenceAbbr == l.ConferenceAbbr;

            // Played = has a score AND within the cutoff week
            var playedConfGames = allGames
                .Where(g => IsConfGame(g) &&
                            (g.WPoints > 0 || g.LPoints > 0) &&
                            (!throughWeek.HasValue || g.Week <= throughWeek.Value))
                .ToList();

            // Unplayed = no score OR beyond the cutoff week
            var unplayedConfGames = allGames
                .Where(g => IsConfGame(g) &&
                            (g.WPoints == 0 && g.LPoints == 0 ||
                             throughWeek.HasValue && g.Week > throughWeek.Value))
                .ToList();

            // ── Project unplayed conference games ─────────────────────────────────
            var matchupRequests = unplayedConfGames
                .Where(g => teams.ContainsKey(g.WinnerId) && teams.ContainsKey(g.LoserId))
                .Select(g => new MatchupRequest
                {
                    TeamName = teams[g.WinnerId].TeamName,
                    OpponentName = teams[g.LoserId].TeamName,
                    Location = g.Location,
                    Week = g.Week
                })
                .ToList();

            var predictions = matchupRequests.Any()
                ? await _predictionService.PredictMatchups(year, matchupRequests, token)
                : new List<GamePrediction>();

            // Key: "TeamName|OpponentName" → prediction
            var predictionMap = predictions.ToDictionary(
                p => $"{p.TeamName}|{p.OpponentName}",
                p => p
            );

            // Resolve projected winner for each unplayed game
            // Returns (WinnerId, LoserId) tuples
            var projectedResults = new List<(int WinnerId, int LoserId)>();

            foreach (var g in unplayedConfGames)
            {
                if (!teams.TryGetValue(g.WinnerId, out var wTeam)) continue;
                if (!teams.TryGetValue(g.LoserId, out var lTeam)) continue;

                var key = $"{wTeam.TeamName}|{lTeam.TeamName}";
                var altKey = $"{lTeam.TeamName}|{wTeam.TeamName}";

                if (predictionMap.TryGetValue(key, out var pred))
                {
                    // WinnerId team is TeamName in prediction
                    if (pred.PredictedTeamScore >= pred.PredictedOpponentScore)
                        projectedResults.Add((g.WinnerId, g.LoserId));
                    else
                        projectedResults.Add((g.LoserId, g.WinnerId));
                }
                else if (predictionMap.TryGetValue(altKey, out var altPred))
                {
                    // WinnerId team is OpponentName in prediction
                    if (altPred.PredictedOpponentScore >= altPred.PredictedTeamScore)
                        projectedResults.Add((g.WinnerId, g.LoserId));
                    else
                        projectedResults.Add((g.LoserId, g.WinnerId));
                }
                else
                {
                    // No prediction — default to listed winner (home field advantage baked in)
                    projectedResults.Add((g.WinnerId, g.LoserId));
                }
            }

            // ── Build standings — actual + projected ──────────────────────────────
            var confRecords = teams.Values
                .Where(t => t.Division == "FBS" && !string.IsNullOrEmpty(t.ConferenceAbbr))
                .Select(t =>
                {
                    var actualWins = playedConfGames.Count(g => g.WinnerId == t.TeamID);
                    var actualLosses = playedConfGames.Count(g => g.LoserId == t.TeamID);

                    var projWins = projectedResults.Count(r =>
                        r.WinnerId == t.TeamID &&
                        unplayedConfGames.Any(g =>
                            g.WinnerId == r.WinnerId && g.LoserId == r.LoserId));

                    var projLosses = projectedResults.Count(r =>
                        r.LoserId == t.TeamID &&
                        unplayedConfGames.Any(g =>
                            g.WinnerId == r.WinnerId && g.LoserId == r.LoserId));

                    records.TryGetValue(t.TeamID, out var rec);

                    return new ConferenceStanding
                    {
                        TeamId = t.TeamID,
                        TeamName = t.TeamName,
                        Conference = t.ConferenceAbbr,
                        Division = GetDivision(t.TeamName, t.ConferenceAbbr),
                        ConferenceWins = actualWins + projWins,
                        ConferenceLosses = actualLosses + projLosses,
                        OverallWins = rec != null ? (int)rec.Wins : 0,
                        OverallLosses = rec != null ? (int)rec.Losses : 0,
                        ConfPointsFor =
                            playedConfGames.Where(g => g.WinnerId == t.TeamID).Sum(g => g.WPoints) +
                            playedConfGames.Where(g => g.LoserId == t.TeamID).Sum(g => g.LPoints),
                        ConfPointsAgainst =
                            playedConfGames.Where(g => g.WinnerId == t.TeamID).Sum(g => g.LPoints) +
                            playedConfGames.Where(g => g.LoserId == t.TeamID).Sum(g => g.WPoints),
                    };
                })
                .ToList();

            // ── Head-to-head: actual results take precedence over projected ───────
            foreach (var standing in confRecords)
            {
                // Actual H2H
                var playedH2H = playedConfGames
                    .Where(g => g.WinnerId == standing.TeamId || g.LoserId == standing.TeamId)
                    .GroupBy(g => g.WinnerId == standing.TeamId ? g.LoserId : g.WinnerId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Count(game => game.WinnerId == standing.TeamId) >
                             g.Count(game => game.LoserId == standing.TeamId)
                    );

                // Projected H2H — only for opponents not yet played
                var projH2H = projectedResults
                    .Where(r =>
                        (r.WinnerId == standing.TeamId || r.LoserId == standing.TeamId) &&
                        !playedH2H.ContainsKey(
                            r.WinnerId == standing.TeamId ? r.LoserId : r.WinnerId))
                    .GroupBy(r => r.WinnerId == standing.TeamId ? r.LoserId : r.WinnerId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Count(r => r.WinnerId == standing.TeamId) >
                             g.Count(r => r.LoserId == standing.TeamId)
                    );

                standing.HeadToHeadResults = playedH2H
                    .Concat(projH2H)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }

            // ── CommonOpponentWinPct and ConfSOS ──────────────────────────────────
            var recordById = confRecords.ToDictionary(r => r.TeamId);

            foreach (var standing in confRecords)
            {
                var wins = standing.HeadToHeadResults.Values.Count(v => v);
                var total = standing.HeadToHeadResults.Count;
                standing.CommonOpponentWinPct = total > 0 ? (double)wins / total : 0.0;

                var oppWinPcts = standing.HeadToHeadResults.Keys
                    .Where(id => recordById.ContainsKey(id))
                    .Select(id => recordById[id].ConferenceWinPct)
                    .ToList();
                standing.ConferenceOpponentWinPct = oppWinPcts.Any()
                    ? oppWinPcts.Average() : 0.0;
            }

            // ── Filter and group ──────────────────────────────────────────────────
            return confRecords
                .Where(s => s.Conference != "IND" &&
                            s.Conference != "Pac-12" &&
                            !string.IsNullOrEmpty(s.Conference))
                .GroupBy(s => s.Conference)
                .ToDictionary(g => g.Key, g => g.ToList());
        }


        // Helper — maps team to their division for Sun Belt


        private static string GetDivision(string teamName, string conference)
        {
            if (conference == "Sun Belt")
            {
                var east = new HashSet<string>
        {
            "App State", "Coastal Carolina", "Georgia Southern",
            "Georgia State", "James Madison", "Marshall",
            "Old Dominion", "South Alabama", "Southern Miss"
        };
                return east.Contains(teamName) ? "East" : "West";
            }
            
            return null; // No division
        }
    }

    /// <summary>
    /// Request model for batch matchup predictions.
    /// </summary>
    public class MatchupBatchRequest
    {
        public int Year { get; set; }
        public List<NCAA_Power_Ratings.Services.MatchupRequest> Matchups { get; set; } = new();
    }
}
