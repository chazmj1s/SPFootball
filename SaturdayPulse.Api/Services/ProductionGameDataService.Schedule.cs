using Microsoft.Extensions.Caching.Memory;
using SaturdayPulse.Contracts;
using SaturdayPulse.Contracts.Requests;
using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.Interfaces;
using SaturdayPulse.Models;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using SaturdayPulse.Utilities;
using SQLitePCL;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Timers;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// ProductionGameDataService — Schedule.
    /// The Schedule/Games tab feed and its supporting week/conference metadata.
    /// </summary>
    public partial class ProductionGameDataService
    {

        // ── Schedule ─────────────────────────────────────────────────────────────

        /// <summary>
        /// V2: reads from Games + Teams tables (CFBD-sourced).
        /// Legacy equivalent: GetScheduleAsync() which reads from Game + Team tables.
        ///
        /// Winner/loser derived from home/away points:
        ///   - Played: higher score = winner; NeutralSite → Location 'N'
        ///   - Unplayed (null/0 points): home team defaults to winner, Location 'W'
        /// HomeId/HomeName/AwayId/AwayName are passed through for future view rebinding.
        /// </summary>
        public async Task<ScheduleResult> GetScheduleV2Async(int? year, CancellationToken token = default)
        {
            var targetYear = year ?? DateTime.Now.Year;

            var games = await _uow.Games.GetByYearAsync(targetYear, token);
            games = games.OrderBy(g => g.Week).ToList();

            if (games.Count == 0) return new ScheduleResult(Array.Empty<object>());

            var confLookup = await _uow.Conferences.GetDictionaryAsync(token);
            var teams = await _uow.Teams.GetDictionaryByTeamIdAsync(token);

            // Year-aware conf + tier for every team in this schedule — one query
            var allTeamIdsForConf = games
                .SelectMany(g => new[] { g.HomeId ?? 0, g.AwayId ?? 0 })
                .Where(id => id > 0)
                .Distinct();

            var confTierByTeamId = await _tierService.GetConfAndTierBatchAsync(
                allTeamIdsForConf, targetYear, token);

            // Safe lookup with static fallback for teams missing a history row
            (string abbr, string tier) ConfTier(int? teamId, string? teamName = null)
            {
                if (teamId.HasValue && confTierByTeamId.TryGetValue(teamId.Value, out var ct))
                    return ct;
                return (string.Empty, ConferenceTierService.GetTierStatic(null, teamName));
            }

            var allProjections = await _projectionCache.GetAllPregameProjections(targetYear, token);

            // Rivalry Notes — fetched once, same batch-fetch pattern as
            // allProjections/rankingsByWeek below, not queried per-game.
            var allRivalries = await _uow.Lookups.GetMatchupHistoriesAsync(token);
            var rivalryLookup = allRivalries.ToDictionary(r => (r.Team1Id, r.Team2Id));

            // ── WeeklyRankings lookup ─────────────────────────────────────────
            var availableWeeks = (await _uow.WeeklyRankings
                .GetDistinctYearWeeksAsync(token))
                .Where(yw => yw.Year == targetYear)
                .Select(yw => yw.Week)
                .OrderBy(w => w)
                .ToList();

            var maxAvailableWeek = availableWeeks.Any() ? availableWeeks.Max() : 0;

            int LookupWeek(int gameWeek)
            {
                var desired = Math.Max(0, gameWeek - 1);
                return availableWeeks.Contains(desired) ? desired : maxAvailableWeek;
            }

            var allTeamIds = games.SelectMany(g => new[] { g.HomeId ?? 0, g.AwayId ?? 0 })
                                           .Where(id => id > 0).Distinct().ToList();
            var distinctLookupWeeks = games.Select(g => LookupWeek(g.Week)).Distinct().ToList();

            var rankingsByWeek = new Dictionary<int, Dictionary<int, WeeklyRanking>>();
            foreach (var w in distinctLookupWeeks)
            {
                rankingsByWeek[w] = await _uow.WeeklyRankings
                    .GetByTeamsAndYearAndWeekAsync(allTeamIds, targetYear, w, token);
            }

            // ── Vegas lines lookup ────────────────────────────────────────────
            // One call per distinct game week, keyed by GameId
            var distinctGameWeeks = games.Select(g => g.Week).Distinct().ToList();
            var linesByGameId = new Dictionary<int, List<Lines>>();

            foreach (var w in distinctGameWeeks)
            {
                var weekLines = await _uow.Lines.GetByYearAndWeekAsync(targetYear, w, token);
                foreach (var line in weekLines)
                {
                    if (!linesByGameId.ContainsKey(line.GameId))
                        linesByGameId[line.GameId] = new List<Lines>();
                    linesByGameId[line.GameId].Add(line);
                }
            }

            // ── Build results ─────────────────────────────────────────────────
            var results = games.Select(g =>
            {
                teams.TryGetValue(g.HomeId ?? 0, out var homeTeam);
                teams.TryGetValue(g.AwayId ?? 0, out var awayTeam);

                var (homeConfAbbr, homeTier) = ConfTier(g.HomeId, g.HomeName);
                var (awayConfAbbr, awayTier) = ConfTier(g.AwayId, g.AwayName);

                var homePoints = g.HomePoints ?? 0;
                var awayPoints = g.AwayPoints ?? 0;
                var isPlayed = homePoints > 0 || awayPoints > 0;
                var actualOU = homePoints + awayPoints;
                char location = g.NeutralSite == true ? 'N' : 'H';
                bool homeWon = homePoints >= awayPoints;

                bool isFinal = !string.IsNullOrEmpty(g.Status)
                    ? g.Status.Equals("completed", StringComparison.OrdinalIgnoreCase)
                    : isPlayed;

                bool isInProgress = !string.IsNullOrEmpty(g.Status)
                    && g.Status.Equals("in_progress", StringComparison.OrdinalIgnoreCase);


                double? projHome = null, projAway = null, projMargin = null;
                bool predWin = false;
                if (allProjections.TryGetValue(g.GameId, out var pred))
                {
                    projHome = Math.Max(0, Math.Round(pred.PredictedTeamScore, 1));
                    projAway = Math.Max(0, Math.Round(pred.PredictedOpponentScore, 1));
                    predWin = pred.IsTeamProjectedWinner;
                    projMargin = Math.Round(pred.ExpectedMargin, 1);
                }


                var projOU = projHome.HasValue && projAway.HasValue
                    ? (double?)Math.Round(projHome.Value + projAway.Value, 1) : null;

                // Rivalry Notes — normalized pair lookup, same normalization
                // MatchupHistoryCalculator used to store the table (lower ID first).
                rivalryLookup.TryGetValue((Math.Min(g.HomeId ?? 0, g.AwayId ?? 0), Math.Max(g.HomeId ?? 0, g.AwayId ?? 0)),
                    out var rivalryForGame);

                var team1 = rivalryForGame?.Team1Id == g.HomeId ? g.HomeName : g.AwayName;
                var team2 = rivalryForGame?.Team2Id == g.AwayId ? g.AwayName : g.HomeName;

                var rivalryNotes = BuildRivalryNotes(
                    rivalryForGame,
                    isFinal,
                    isInProgress,
                    actualMargin: (isFinal || isInProgress) ? (double?)Math.Abs(homePoints - awayPoints) : null,
                    actualTotal: (isFinal || isInProgress) ? (double?)actualOU : null,
                    projectedMargin: (projHome.HasValue && projAway.HasValue)
                                          ? (double?)Math.Abs(projHome.Value - projAway.Value) : null,
                    projectedTotal: projOU,
                    team1: team1,
                    team2: team2,
                    winner: ((isFinal || isInProgress) ? homeWon : predWin) ? g.HomeName : g.AwayName);
                // ── Team stats ────────────────────────────────────────────────
                var lookupWeek = LookupWeek(g.Week);
                rankingsByWeek.TryGetValue(lookupWeek, out var snapshot);

                object? homeStats = null;
                object? awayStats = null;

                // REMOVED 2026-08-20: isWeek1 flag that forced OverallRank/Record/
                // PowerRating/CombinedSOS/OffensiveRank/AvgPointsScored/
                // OffensiveZScore/DefensiveRank/AvgPointsAllowed/DefensiveZScore
                // to 0/null/"0-0" for every Week 1 game, every season. Legacy
                // guard from before full-season blending existed and before
                // week 0 was introduced — WeeklyRankings now has real, meaningful
                // computed values at week 1 (and week 0), so there's no longer
                // any reason to blank them on display. Confirmed with Charlie.

                if (snapshot != null)
                {
                    if (snapshot.TryGetValue(g.HomeId ?? 0, out var hwr))
                        homeStats = new
                        {
                            TeamId = hwr.TeamID,
                            TeamName = homeTeam?.TeamName ?? g.HomeName,
                            OverallRank = hwr.OverallRank,
                            Record = $"{hwr.Wins}-{hwr.Losses}",
                            PowerRating = (double?)hwr.Ranking,
                            CombinedSOS = (double?)hwr.CombinedSOS,
                            OffensiveRank = hwr.OffensiveRank,
                            AvgPointsScored = (double?)hwr.AvgPointsScored,
                            OffensiveZScore = (double?)hwr.OffensiveZScore,
                            DefensiveRank = hwr.DefensiveRank,
                            AvgPointsAllowed = (double?)hwr.AvgPointsAllowed,
                            DefensiveZScore = (double?)hwr.DefensiveZScore,
                        };

                    if (snapshot.TryGetValue(g.AwayId ?? 0, out var awr))
                        awayStats = new
                        {
                            TeamId = awr.TeamID,
                            TeamName = awayTeam?.TeamName ?? g.AwayName,
                            OverallRank = awr.OverallRank,
                            Record = $"{awr.Wins}-{awr.Losses}",
                            PowerRating = (double?)awr.Ranking,
                            CombinedSOS = (double?)awr.CombinedSOS,
                            OffensiveRank = awr.OffensiveRank,
                            AvgPointsScored = (double?)awr.AvgPointsScored,
                            OffensiveZScore = (double?)awr.OffensiveZScore,
                            DefensiveRank = awr.DefensiveRank,
                            AvgPointsAllowed = (double?)awr.AvgPointsAllowed,
                            DefensiveZScore = (double?)awr.DefensiveZScore,
                        };
                }

                // ── Vegas lines — average across providers ────────────────────
                object? vegasLines = null;
                if (linesByGameId.TryGetValue(g.GameId, out var gameLines) && gameLines.Count > 0)
                {
                    var spreads = gameLines.Where(l => l.Spread.HasValue).Select(l => l.Spread!.Value).ToList();
                    var spreadsOpen = gameLines.Where(l => l.SpreadOpen.HasValue).Select(l => l.SpreadOpen!.Value).ToList();
                    var ous = gameLines.Where(l => l.OverUnder.HasValue).Select(l => l.OverUnder!.Value).ToList();
                    var ousOpen = gameLines.Where(l => l.OverUnderOpen.HasValue).Select(l => l.OverUnderOpen!.Value).ToList();
                    var homeMoneylines = gameLines.Where(l => l.HomeMoneyline.HasValue).Select(l => l.HomeMoneyline!.Value).ToList();
                    var awayMoneylines = gameLines.Where(l => l.AwayMoneyline.HasValue).Select(l => l.AwayMoneyline!.Value).ToList();

                    vegasLines = new
                    {
                        Spread = spreads.Count > 0 ? (decimal?)Math.Round(spreads.Average(), 1) : null,
                        SpreadOpen = spreadsOpen.Count > 0 ? (decimal?)Math.Round(spreadsOpen.Average(), 1) : null,
                        OverUnder = ous.Count > 0 ? (decimal?)Math.Round(ous.Average(), 1) : null,
                        OverUnderOpen = ousOpen.Count > 0 ? (decimal?)Math.Round(ousOpen.Average(), 1) : null,
                        HomeMoneyline = homeMoneylines.Count > 0 ? (int?)Math.Round(homeMoneylines.Average()) : null,
                        AwayMoneyline = awayMoneylines.Count > 0 ? (int?)Math.Round(awayMoneylines.Average()) : null,
                        ProviderCount = gameLines.Count,
                    };
                }

                return (object)new
                {
                    Id = g.GameId,
                    g.Year,
                    g.Week,
                    GameDate = g.GameDate,
                    GameDay = g.GameDay,
                    GameTime = g.KickoffTime,
                    Status = g.Status,
                    Clock = g.Clock,
                    period = g.Period,
                    homeLineScores = g.HomeLineScores,
                    awayLineScores = g.AwayLineScores,
                    HomeName = g.HomeName,
                    HomeId = g.HomeId,
                    HomeConf = homeConfAbbr,
                    HomeTier = homeTier,
                    HomePoints = homePoints,
                    HomeProjScore = projHome,
                    AwayName = g.AwayName,
                    AwayId = g.AwayId,
                    AwayConf = awayConfAbbr,
                    AwayTier = awayTier,
                    AwayPoints = awayPoints,
                    AwayProjScore = projAway,
                    Location = location,
                    IsPlayed = isPlayed,
                    ActualOU = actualOU,
                    ProjOU = projOU,
                    ProjMargin = projMargin,
                    SeasonType = g.SeasonType,
                    HomeStats = homeStats,
                    AwayStats = awayStats,
                    VegasLines = vegasLines,
                    RivalryNotes = rivalryNotes,

                    // Legacy fields
                    WinnerName = homePoints >= awayPoints ? g.HomeName : g.AwayName,
                    WinnerShortName = homePoints >= awayPoints ? g.HomeName : g.AwayName,
                    WinnerId = homePoints >= awayPoints ? g.HomeId : g.AwayId,
                    WinnerConf = homeWon ? homeConfAbbr : awayConfAbbr,
                    WinnerTier = homeWon ? homeTier : awayTier,
                    WPoints = homePoints >= awayPoints ? homePoints : awayPoints,
                    LoserName = homePoints >= awayPoints ? g.AwayName : g.HomeName,
                    LoserShortName = homePoints >= awayPoints ? g.AwayName : g.HomeName,
                    LoserId = homePoints >= awayPoints ? g.AwayId : g.HomeId,
                    LoserConf = homeWon ? awayConfAbbr : homeConfAbbr,
                    LoserTier = homeWon ? awayTier : homeTier,
                    LPoints = homePoints >= awayPoints ? awayPoints : homePoints,
                };
            }).ToList();

            return new ScheduleResult(results);
        }


        // ── Team Schedule ────────────────────────────────────────────────────────

        /// <summary>
        /// V2: reads from Games + Teams + Conferences tables (CFBD-sourced).
        /// Legacy equivalent: GetTeamScheduleAsJsonAsync() which reads from Game + Team.
        /// Returns the full season schedule for a single team with actual and projected scores.
        /// </summary>
        public async Task<TeamScheduleV2Result> GetTeamScheduleV2Async(
            int teamId, int year, CancellationToken token = default)
        {
            var Teams    = await _uow.Teams.GetDictionaryByTeamIdAsync(token);
            var confLookup = await _uow.Conferences.GetDictionaryAsync(token);

            if (!Teams.TryGetValue(teamId, out var team))
                throw new KeyNotFoundException($"Team {teamId} not found.");

            string ConfAbbr(Teams? t)
            {
                if (t?.ConferenceId == null) return string.Empty;
                confLookup.TryGetValue(t.ConferenceId.Value, out var conf);
                return conf?.Abbreviation ?? string.Empty;
            }

            var teamRecord = await _uow.TeamRecords.GetByTeamAndYearAsync(teamId, (short)year, token);
            var allGames   = await _uow.Games.GetByYearAsync(year, token);

            var teamGames = allGames
                .Where(g => g.HomeId == teamId || g.AwayId == teamId)
                .OrderBy(g => g.Week)
                .ToList();

            var allProjections = await _projectionCache.GetAllPregameProjections(year, token);

            // Rivalry Notes — same batch-fetch pattern as GetScheduleV2Async.
            var allRivalries = await _uow.Lookups.GetMatchupHistoriesAsync(token);
            var rivalryLookup = allRivalries.ToDictionary(r => (r.Team1Id, r.Team2Id));

            var games = teamGames.Select(g =>
            {
                bool isHome  = g.HomeId == teamId;
                var oppId    = isHome ? (g.AwayId ?? 0) : (g.HomeId ?? 0);
                Teams.TryGetValue(oppId, out var opp);
                var opponent  = opp?.TeamName ?? opp?.Abbreviation ?? "Unknown";
                var oppConf  = ConfAbbr(opp);

                bool isPlayed = (g.HomePoints ?? 0) > 0 || (g.AwayPoints ?? 0) > 0;

                bool isFinal = !string.IsNullOrEmpty(g.Status)
                    ? g.Status.Equals("completed", StringComparison.OrdinalIgnoreCase)
                    : isPlayed;

                bool isInProgress = !string.IsNullOrEmpty(g.Status)
                    && g.Status.Equals("in_progress", StringComparison.OrdinalIgnoreCase);

                int myPts = isHome ? (g.HomePoints ?? 0) : (g.AwayPoints ?? 0);
                int oppPts = isHome ? (g.AwayPoints ?? 0) : (g.HomePoints ?? 0);
                bool won = myPts > oppPts;

                double? projMy = null, projOpp = null;
                bool predWin = false;
                string confidence = "Unknown";
                if (allProjections.TryGetValue(g.GameId, out var pred))
                {
                    projMy = isHome ? pred.PredictedTeamScore : pred.PredictedOpponentScore;
                    projOpp = isHome ? pred.PredictedOpponentScore : pred.PredictedTeamScore;
                    predWin = projMy > projOpp;
                    confidence = pred.Confidence ?? "Unknown";
                }

                // Rivalry Notes — normalized pair lookup, same normalization
                // MatchupHistoryCalculator used to store the table (lower ID first).
                rivalryLookup.TryGetValue(
                    (Math.Min(teamId, oppId), Math.Max(teamId, oppId)),
                    out var rivalryForGame);

                var team1 = rivalryForGame?.Team1Id == g.HomeId ? g.HomeName : g.AwayName;
                var team2 = rivalryForGame?.Team2Id == g.AwayId ? g.AwayName : g.HomeName;

                var rivalryNotes = BuildRivalryNotes(
                    rivalryForGame,
                    isFinal,
                    isInProgress,
                    actualMargin: (isFinal || isInProgress) ? (double?)Math.Abs(myPts - oppPts) : null,
                    actualTotal: (isFinal || isInProgress) ? (double?)(myPts + oppPts) : null,
                    projectedMargin: (projMy.HasValue && projOpp.HasValue)
                                         ? (double?)Math.Abs(projMy.Value - projOpp.Value) : null,
                    projectedTotal: (projMy.HasValue && projOpp.HasValue)
                                         ? (double?)(projMy.Value + projOpp.Value) : null,
                    team1: team1,
                    team2: team2,
                    winner: (((isFinal || isInProgress) ? won : predWin) == isHome) ? g.HomeName : g.AwayName);
                return (object)new
                {
                    g.Week,
                    GameDate   = g.GameDate,
                    GameDay    = g.GameDay,
                    Opponent   = opponent,
                    OpponentId = oppId,
                    OpponentConf = oppConf,
                    Location   = isHome ? "vs" : "@",
                    NeutralSite = g.NeutralSite == true,
                    Result     = isPlayed ? (won ? "W" : "L") : (string?)null,
                    Score      = isPlayed ? $"{myPts}-{oppPts}" : null,
                    ProjScore  = projMy.HasValue
                        ? $"{(int)Math.Round(projMy.Value)}-{(int)Math.Round(projOpp!.Value)}" : null,
                    Confidence = isPlayed ? null : confidence,
                    Type       = isPlayed ? "Actual" : "Projected",
                    SeasonType = g.SeasonType,
                    RivalryNotes = rivalryNotes
                };
            }).ToList();

            var summary = teamRecord != null ? (object)new
            {
                Year          = year,
                TeamName      = team.TeamName,
                Conference    = ConfAbbr(team),
                Wins          = (int)teamRecord.Wins,
                Losses        = (int)teamRecord.Losses,
                PointsFor     = teamRecord.PointsFor,
                PointsAgainst = teamRecord.PointsAgainst
            } : null;

            return new TeamScheduleV2Result(summary, games);
        }


        public async Task<List<PlayedWeekDto>> GetPlayedWeeksByYearAsync(int year, CancellationToken token = default)
        {
            var result = await _uow.Games.GetPlayedWeeksByYearAsync(year, token);
            return result;
        }


        /// <summary>
        /// Returns the distinct FBS conferences that had at least one team active
        /// in the given year, ordered P4 → G5, then alphabetically within tier.
        ///
        /// Used to populate the conference filter dropdown on year change so that
        /// historical conferences (SWC, Big 8, Pac-10, Big East) appear in the
        /// correct years rather than the static current-era list.
        /// </summary>
        public async Task<List<ConferenceInfo>> GetConferencesForYearAsync(
            int year, CancellationToken token = default)
        {
            // Each repository touches only its own table
            var confIdByTeamId = await _uow.TeamsConferenceHistory
                .GetConferenceIdsByYearAsync(year, token);

            var confById = await _uow.Conferences.GetDictionaryAsync(token);

            var activeConfIds = confIdByTeamId.Values.Distinct().ToHashSet();

            var list = activeConfIds
                .Where(id => confById.ContainsKey(id))
                .Select(id =>
                {
                    var c = confById[id];
                    var tier = _tierService.ClassifyConference(c.ConferenceId, c.Classification, year);
                    return new ConferenceInfo
                    {
                        Name = c.Name ?? string.Empty,
                        Abbreviation = c.Abbreviation ?? string.Empty,
                        Tier = tier
                    };
                })
                .Where(c => c.Tier is "P4" or "G6")    // FBS only — FCS/DII/DIII are noise
                .OrderBy(c => c.Tier == "P4" ? 0 : 1)  // P4 first
                .ThenBy(c => c.Name)
                .ToList();

            list.Insert(0, new ConferenceInfo { Name = "All", Abbreviation = "All", Tier = "All" });
            return list;
        }
    }
}
