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
    /// ProductionGameDataService — Postseason.
    /// Conference championship qualifiers (real and projected), conference
    /// standings construction, and bowl/playoff schedule feeds. The largest
    /// and most actively-changing cluster — see engineering notes for the
    /// championship-week / standings-cutoff history here.
    /// </summary>
    public partial class ProductionGameDataService
    {

        // ── Conference Standings and Projections ─────────────────────────────────

        /// <summary>
        /// V2: Championship qualifiers from Games + TeamsConferenceHistory tables.
        /// Legacy equivalent: GetChampionshipQualifiersAsync().
        /// </summary>
        public async Task<ChampionshipQualifiersResult> GetChampionshipQualifiersV2Async(
            int? year, CancellationToken token = default)
        {
            var targetYear            = year ?? DateTime.Now.Year;
            var standingsByConference = await BuildConferenceStandingsV2Async(targetYear, token);
            var service               = new ConferenceChampionshipService();
            var results               = BuildQualifierResponse(standingsByConference, service, includeContenders: false);
            return new ChampionshipQualifiersResult(results);
        }


        /// <summary>
        /// V2: Projected championship qualifiers from Games + TeamsConferenceHistory tables.
        /// Legacy equivalent: GetProjectedChampionshipQualifiersAsync().
        ///
        /// When CFBD has published the scheduled title game for a conference,
        /// the matchup includes a Game object shaped identically to GetScheduleV2Async
        /// (projected scores, team stats, Vegas lines). The client renders the
        /// game card above the qualifier rows.
        /// </summary>
        public async Task<ChampionshipQualifiersResult> GetProjectedChampionshipQualifiersV2Async(
            int? year, int? throughWeek, CancellationToken token = default)
        {
            var targetYear = year ?? DateTime.Now.Year;
            var standingsByConference = await BuildProjectedConferenceStandingsV2Async(targetYear, throughWeek, token);
            var service = new ConferenceChampionshipService();
            var qualifierResults = BuildQualifierResponse(standingsByConference, service, includeContenders: true, throughWeek: throughWeek);

            // ── TeamId lookup per conference — needed to match the title game ─────
            // BuildQualifierResponse anonymous-types Qualifier1/2 and drops TeamId,
            // so we capture IDs here directly from standingsByConference.
            // Carries the full ConferenceStanding, not just TeamId, so the
            // "higher seed hosts" projection below (for conferences with no
            // real title game posted yet) has what it needs without a second
            // standings lookup.
            var qualifiersByConference = standingsByConference
                .Where(kvp => kvp.Value.Count >= 2)
                .Select(kvp => service.GetQualifiers(kvp.Key, kvp.Value))
                .Where(r => r.Qualifier1 != null && r.Qualifier2 != null)
                .ToDictionary(
                    r => r.Conference,
                    r => (Q1: r.Qualifier1, Q2: r.Qualifier2));

            // ── Supporting data for the game card — same lookups as GetScheduleV2Async ─
            var allGames = await _uow.Games.GetByYearAsync(targetYear, token);
            var teams = await _uow.Teams.GetDictionaryByTeamIdAsync(token);
            var confLookup = await _uow.Conferences.GetDictionaryAsync(token);
            var allProjections = await _projectionCache.GetAllProjections(targetYear, token);

            string GetConfAbbr(Teams? t)
            {
                if (t?.ConferenceId == null) return string.Empty;
                confLookup.TryGetValue(t.ConferenceId.Value, out var conf);
                return conf?.Abbreviation ?? string.Empty;
            }

            // ── WeeklyRankings lookup — same shape as GetScheduleV2Async ──────────
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

            // Computed first — the real title game can only be identified by
            // restricting to this week; two conference members may well have
            // already played each other earlier in the regular season too.
            var championshipWeek = ChampionshipWeekCalculator.GetChampionshipWeek(targetYear, allGames);

            // Find candidate title games up front (so we know which weeks to query
            // for stats and lines — typically just the conference championship week)
            var titleGamesByConf = new Dictionary<string, Games>();
            if (championshipWeek.HasValue)
            {
                foreach (var kvp in qualifiersByConference)
                {
                    var (q1, q2) = kvp.Value;
                    var game = allGames.FirstOrDefault(g =>
                        g.Week == championshipWeek.Value &&
                        ((g.HomeId == q1.TeamId && g.AwayId == q2.TeamId) ||
                         (g.HomeId == q2.TeamId && g.AwayId == q1.TeamId)));
                    if (game != null) titleGamesByConf[kvp.Key] = game;
                }
            }
            // If championshipWeek isn't known yet (rivalry week games not
            // posted), titleGamesByConf stays empty — every conference falls
            // through to the projected-matchup branch below rather than
            // risking a match against the wrong week.

            var titleGames = titleGamesByConf.Values.ToList();
            var titleGameWeeks = titleGames.Select(g => g.Week).Distinct().ToList();

            // ── WeeklyRankings snapshots for title game teams ────────────────────
            var titleGameTeamIds = titleGames
                .SelectMany(g => new[] { g.HomeId ?? 0, g.AwayId ?? 0 })
                .Where(id => id > 0).Distinct().ToList();

            // Batched, year-aware tier lookup for the title-game card — replaces the
            // old year-blind GetConfName + RatingCalculator.GetConferenceTier pair.
            // BuildTitleGameObject is synchronous, so this must be fetched up front
            // rather than awaited inside it.
            var titleGameTierByTeamId = await _tierService.GetConfDataBatchAsync(
                titleGameTeamIds, targetYear, token);
            string GetTier(Teams? t) =>
                t != null && titleGameTierByTeamId.TryGetValue(t.TeamId, out var cd)
                    ? cd.Tier
                    : ConferenceTierService.GetTierStatic(null, t?.TeamName);

            var rankingsByWeek = new Dictionary<int, Dictionary<int, WeeklyRanking>>();
            if (titleGameTeamIds.Count > 0)
            {
                var distinctLookupWeeks = titleGames.Select(g => LookupWeek(g.Week)).Distinct().ToList();
                foreach (var w in distinctLookupWeeks)
                {
                    rankingsByWeek[w] = await _uow.WeeklyRankings
                        .GetByTeamsAndYearAndWeekAsync(titleGameTeamIds, targetYear, w, token);
                }
            }

            // ── Vegas lines — same year-and-week aggregation as GetScheduleV2Async ──
            var linesByGameId = new Dictionary<int, List<Lines>>();
            foreach (var w in titleGameWeeks)
            {
                var weekLines = await _uow.Lines.GetByYearAndWeekAsync(targetYear, w, token);
                foreach (var line in weekLines)
                {
                    if (!linesByGameId.ContainsKey(line.GameId))
                        linesByGameId[line.GameId] = new List<Lines>();
                    linesByGameId[line.GameId].Add(line);
                }
            }

            // ── Build the enriched response ──────────────────────────────────────
            var enriched = new List<object>();

            foreach (var raw in qualifierResults)
            {
                var r = (dynamic)raw;
                var conf = (string)r.Conference;

                object? gameObj = null;
                object? projectedMatchup = null;

                if (titleGamesByConf.TryGetValue(conf, out var titleGame))
                {
                    // r.Contenders/r.TiebreakerLog are `dynamic` (r itself is
                    // dynamic) — passing them inline makes the WHOLE call
                    // dynamically dispatched, and a bare method group
                    // (LookupWeek/GetConfAbbr/GetTier below) can't convert to
                    // its delegate type under dynamic dispatch (CS1976).
                    // Assigning to typed locals first resolves them to plain
                    // static types, so the call itself stays statically bound.
                    object contendersForGame = r.Contenders;
                    List<string> tiebreakerLogForGame = r.TiebreakerLog;

                    gameObj = BuildTitleGameObject(
                        titleGame, teams, allProjections, rankingsByWeek,
                        linesByGameId, LookupWeek, GetConfAbbr, GetTier,
                        contendersForGame, tiebreakerLogForGame);
                }
                else if (championshipWeek.HasValue && qualifiersByConference.TryGetValue(conf, out var pair))
                {
                    var (q1, q2) = pair;

                    // Higher seed hosts — approximates most non-neutral-site
                    // championship games; real venue data doesn't exist for a
                    // game that isn't scheduled yet (Charlie, 2026-09-15).
                    var (homeQ, awayQ) = q1.ConferenceWinPct >= q2.ConferenceWinPct
                        ? (q1, q2) : (q2, q1);

                    GamePrediction? pred = null;
                    try
                    {
                        pred = await PredictMatchupAsync(
                            targetYear, homeQ.TeamName, awayQ.TeamName, 'H', championshipWeek.Value, token);
                    }
                    catch (ArgumentException)
                    {
                        // Team name mismatch between ConferenceStanding.TeamName
                        // and the Teams table for one pairing shouldn't fail the
                        // whole response.
                    }

                    if (pred != null)
                    {
                        projectedMatchup = new
                        {
                            HomeName       = homeQ.TeamName,
                            AwayName       = awayQ.TeamName,
                            HomeProjScore  = Math.Round(pred.PredictedTeamScore, 1),
                            AwayProjScore  = Math.Round(pred.PredictedOpponentScore, 1),
                            ExpectedMargin = Math.Round(pred.ExpectedMargin, 1),
                            Confidence     = pred.Confidence,
                            Week           = championshipWeek.Value,
                        };
                    }
                }

                enriched.Add(new
                {
                    r.Conference,
                    r.Format,
                    r.Qualifier1,
                    r.Qualifier2,
                    r.Qualifier1Method,
                    r.Qualifier2Method,
                    r.TiebreakerLog,
                    r.StubsApplied,
                    r.SimulatedThrough,
                    r.Contenders,
                    Game = gameObj,
                    ProjectedMatchup = projectedMatchup,
                });
            }

            return new ChampionshipQualifiersResult(enriched);
        }

        /// <summary>
        /// V2: Projected conference standings for all FBS teams.
        /// Legacy equivalent: GetProjectedStandingsAsync() which reads from Game + Team tables.
        /// Uses ConferenceGame flag for conference game detection.
        /// Uses TeamsConferenceHistory to resolve conference for the target year.
        /// </summary>
        public async Task<IReadOnlyList<object>> GetProjectedStandingsV2Async(
            int? year, int? throughWeek, string? conference, CancellationToken token = default)
        {
            var targetYear = year ?? DateTime.Now.Year;
            var Teams    = await _uow.Teams.GetDictionaryByTeamIdAsync(token);
            var confLookup = await _uow.Conferences.GetDictionaryAsync(token);
            var confByYear = await _uow.TeamsConferenceHistory.GetConferenceIdsByYearAsync(targetYear, token);

            // Helper: get conference abbreviation for a team in the target year
            string ConfAbbrForYear(int teamId)
            {
                if (!confByYear.TryGetValue(teamId, out var confId)) return string.Empty;
                confLookup.TryGetValue(confId, out var conf);
                return conf?.Abbreviation ?? string.Empty;
            }

            var allGames = await _uow.Games.GetByYearAsync(targetYear, token);

            if (allGames.Any())
            {
                var maxWeek = allGames.Max(g => g.Week);
                allGames = allGames.Where(g => g.Week < maxWeek).ToList();
            }

            var allProjections = await _projectionCache.GetAllProjections(targetYear, token);

            // Target teams: FBS, has a conference assignment this year, not IND/Pac-12,
            // optionally filtered by conference param
            var targetTeams = Teams.Values
                .Where(t => string.Equals(t.Division, "fbs", StringComparison.OrdinalIgnoreCase)
                         && confByYear.ContainsKey(t.TeamId))
                .Select(t => new { Team = t, ConfAbbr = ConfAbbrForYear(t.TeamId) })
                .Where(x => !string.IsNullOrEmpty(x.ConfAbbr)
                         && x.ConfAbbr != "IND"
                         && x.ConfAbbr != "Pac-12"
                         && (conference == null ||
                             x.ConfAbbr.Equals(conference, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var teamResults = targetTeams.Select(x =>
            {
                var team     = x.Team;
                var confAbbr = x.ConfAbbr;

                // Conference games for this team this year
                var teamConfGames = allGames
                    .Where(g => g.ConferenceGame == true &&
                                (g.HomeId == team.TeamId || g.AwayId == team.TeamId))
                    .OrderBy(g => g.Week)
                    .ToList();

                int actualWins = 0, actualLosses = 0, projWins = 0, projLosses = 0;

                var gameDetails = teamConfGames.Select(g =>
                {
                    bool isHome   = g.HomeId == team.TeamId;
                    var oppId     = isHome ? (g.AwayId ?? 0) : (g.HomeId ?? 0);
                    Teams.TryGetValue(oppId, out var opp);
                    var oppName   = opp?.Abbreviation ?? opp?.TeamName ?? "Unknown";

                    bool isPlayed = ((g.HomePoints ?? 0) > 0 || (g.AwayPoints ?? 0) > 0) &&
                                    (!throughWeek.HasValue || g.Week <= throughWeek.Value);

                    if (isPlayed)
                    {
                        int myPts  = isHome ? (g.HomePoints ?? 0) : (g.AwayPoints ?? 0);
                        int oppPts = isHome ? (g.AwayPoints ?? 0) : (g.HomePoints ?? 0);
                        bool won   = myPts > oppPts;
                        if (won) actualWins++; else actualLosses++;

                        return (object)new
                        {
                            g.Week, Opponent = oppName, Location = isHome ? "vs" : "@",
                            Result = won ? "W" : "L", Score = $"{myPts}-{oppPts}",
                            ProjScore = (string?)null, Confidence = (string?)null,
                            Type = "Actual", NeutralSite = g.NeutralSite == true
                        };
                    }
                    else
                    {
                        double projMyScore = 0, projOppScore = 0;
                        string confidence  = "Unknown";
                        bool projWin       = false;

                        if (allProjections.TryGetValue(g.GameId, out var pred))
                        {
                            projMyScore  = isHome ? pred.PredictedTeamScore     : pred.PredictedOpponentScore;
                            projOppScore = isHome ? pred.PredictedOpponentScore  : pred.PredictedTeamScore;
                            confidence   = pred.Confidence ?? "Unknown";
                            projWin      = isHome ? pred.IsTeamProjectedWinner : !pred.IsTeamProjectedWinner;
                        }
                        else { projWin = isHome; }   // home default

                        if (projWin) projWins++; else projLosses++;

                        return (object)new
                        {
                            g.Week, Opponent = oppName, Location = isHome ? "vs" : "@",
                            Result = projWin ? "W" : "L", Score = (string?)null,
                            ProjScore = projMyScore > 0
                                ? $"{Math.Round(projMyScore)}-{Math.Round(projOppScore)}" : null,
                            Confidence = confidence, Type = "Projected",
                            NeutralSite = g.NeutralSite == true
                        };
                    }
                }).ToList();

                int totalWins   = actualWins   + projWins;
                int totalLosses = actualLosses + projLosses;
                int total       = totalWins + totalLosses;

                return (object)new
                {
                    team.TeamName,
                    Conference      = confAbbr,
                    Division        = RatingCalculator.GetDivision(team.TeamName, confAbbr),
                    ActualWins      = actualWins,
                    ActualLosses    = actualLosses,
                    ProjectedWins   = totalWins,
                    ProjectedLosses = totalLosses,
                    ProjectedWinPct = Math.Round(total > 0 ? (double)totalWins / total : 0.0, 3),
                    Games           = gameDetails,
                    SimulatedThrough = throughWeek.HasValue ? $"Week {throughWeek}" : "Current"
                };
            }).ToList();

            return teamResults
                .OrderBy(t  => RatingCalculator.ConferenceDisplayOrder(((dynamic)t).Conference))
                .ThenByDescending(t => ((dynamic)t).ProjectedWinPct)
                .ThenByDescending(t => ((dynamic)t).ProjectedWins)
                .ToList();
        }


        // ── Postseason ───────────────────────────────────────────────────────────
        public async Task<ScheduleResult> GetPostseasonGamesV2Async(int? year, CancellationToken token = default)
        {
            var targetYear = year ?? DateTime.Now.Year;

            var games = await _uow.Games.GetPostSeasonByYear(targetYear, token);
            if (games.Count == 0) return new ScheduleResult(Array.Empty<object>());

            var confLookup = await _uow.Conferences.GetDictionaryAsync(token);
            var teams      = await _uow.Teams.GetDictionaryByTeamIdAsync(token);

            // FBS only — exclude FCS/D2/D3 placeholder rows
            games = games
                .Where(g =>
                {
                    teams.TryGetValue(g.HomeId ?? 0, out var ht);
                    teams.TryGetValue(g.AwayId ?? 0, out var at);
                    return string.Equals(ht?.Division, "fbs", StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(at?.Division, "fbs", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            if (games.Count == 0) return new ScheduleResult(Array.Empty<object>());

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

            var results = games.Select(g =>
            {
                teams.TryGetValue(g.HomeId ?? 0, out var homeTeam);
                teams.TryGetValue(g.AwayId ?? 0, out var awayTeam);

                var (homeConfAbbr, homeTier) = ConfTier(g.HomeId, g.HomeName);
                var (awayConfAbbr, awayTier) = ConfTier(g.AwayId, g.AwayName);
                
                var homePoints = g.HomePoints ?? 0;
                var awayPoints = g.AwayPoints ?? 0;
                var isPlayed   = homePoints > 0 || awayPoints > 0;
                var actualOU   = homePoints + awayPoints;
                char location  = g.NeutralSite == true ? 'N' : 'H';
                bool homeWon = homePoints >= awayPoints;

                double? projHome = null, projAway = null;
                if (allProjections.TryGetValue(g.GameId, out var pred))
                {
                    projHome = Math.Max(0, Math.Round(pred.PredictedTeamScore,     1));
                    projAway = Math.Max(0, Math.Round(pred.PredictedOpponentScore, 1));
                }

                var projOU = projHome.HasValue && projAway.HasValue
                             ? (double?)Math.Round(projHome.Value + projAway.Value, 1) : null;

                return (object)new
                {
                    Id            = g.GameId,
                    g.Year,
                    g.Week,
                    GameDate      = g.GameDate,
                    GameDay       = g.GameDay,
                    HomeName      = g.HomeName,
                    HomeId        = g.HomeId,
                    HomeConf      = homeConfAbbr,
                    HomeTier      = homeTier,
                    HomePoints    = homePoints,
                    HomeProjScore = projHome,
                    AwayName      = g.AwayName,
                    AwayId        = g.AwayId,
                    AwayConf      = awayConfAbbr,
                    AwayTier      = awayTier,
                    AwayPoints    = awayPoints,
                    AwayProjScore = projAway,
                    Location      = location,
                    IsPlayed      = isPlayed,
                    ActualOU      = actualOU,
                    ProjOU        = projOU,
                    SeasonType    = g.SeasonType,
                };
            }).ToList();

            return new ScheduleResult(results);
        }


        /// <summary>
        /// V2: Builds actual conference standings for a year.
        /// Uses ConferenceGame flag on Games for conference game detection.
        /// Uses TeamsConferenceHistory to identify which conference each team was in that year.
        /// Legacy equivalent: BuildConferenceStandingsAsync().
        /// </summary>
        private async Task<Dictionary<string, List<ConferenceStanding>>> BuildConferenceStandingsV2Async(
            int year, CancellationToken token)
        {
            var Teams    = await _uow.Teams.GetDictionaryByTeamIdAsync(token);
            var confLookup = await _uow.Conferences.GetDictionaryAsync(token);
            var confByYear = await _uow.TeamsConferenceHistory.GetConferenceIdsByYearAsync(year, token);
            var records    = await _uow.TeamRecords.GetByYearAsync(year, token);
            var recordById = records.ToDictionary(tr => tr.TeamID);

            var fbsTeamsThisYear = Teams.Values
                .Where(t => string.Equals(t.Division, "fbs", StringComparison.OrdinalIgnoreCase)
                         && confByYear.ContainsKey(t.TeamId))
                .ToList();

            var allGames  = await _uow.Games.GetByYearAsync(year, token);
            var confGames = allGames.Where(g => g.ConferenceGame == true).ToList();

            // Final standings — this builder has no throughWeek of its own,
            // so the internal rating is looked up for the season's last
            // available week rather than a caller-supplied week.
            var lastWeek = allGames.Any() ? allGames.Max(g => g.Week) : (int?)null;
            var overallRankByTeam = await GetOverallRankLookupAsync(year, lastWeek, token);

            var confStandings = fbsTeamsThisYear.Select(t =>
            {
                confByYear.TryGetValue(t.TeamId, out var confId);
                confLookup.TryGetValue(confId, out var conf);
                var confAbbr = conf?.Abbreviation ?? string.Empty;

                var teamConfGames = confGames
                    .Where(g => g.HomeId == t.TeamId || g.AwayId == t.TeamId)
                    .ToList();

                int confWins = 0, confLosses = 0, ptsFor = 0, ptsAgainst = 0;
                var gameScores = new List<ConferenceGameScore>();
                foreach (var g in teamConfGames)
                {
                    bool isHome  = g.HomeId == t.TeamId;
                    int myPts    = isHome ? (g.HomePoints ?? 0) : (g.AwayPoints ?? 0);
                    int oppPts   = isHome ? (g.AwayPoints ?? 0) : (g.HomePoints ?? 0);
                    bool won     = myPts > oppPts || (myPts == 0 && oppPts == 0 && isHome);
                    if (won) confWins++; else confLosses++;
                    ptsFor     += myPts;
                    ptsAgainst += oppPts;

                    var oppId = isHome ? g.AwayId : g.HomeId;
                    if (oppId.HasValue)
                        gameScores.Add(new ConferenceGameScore
                        {
                            OpponentId    = oppId.Value,
                            PointsFor     = myPts,
                            PointsAgainst = oppPts
                        });
                }

                recordById.TryGetValue(t.TeamId, out var rec);
                var overallRank = overallRankByTeam.TryGetValue(t.TeamId, out var orank) ? orank : (int?)null;

                return new ConferenceStanding
                {
                    TeamId                 = t.TeamId,
                    TeamName               = t.TeamName,
                    Conference             = confAbbr,
                    Division               = RatingCalculator.GetDivision(t.TeamName, confAbbr),
                    ConferenceWins         = confWins,
                    ConferenceLosses       = confLosses,
                    ActualConferenceWins   = confWins,
                    ActualConferenceLosses = confLosses,
                    OverallWins            = rec != null ? (int)rec.Wins   : 0,
                    OverallLosses          = rec != null ? (int)rec.Losses : 0,
                    ConfPointsFor          = ptsFor,
                    ConfPointsAgainst      = ptsAgainst,
                    ConferenceGameScores   = gameScores,
                    InternalRatingScore    = overallRank
                };
            }).ToList();

            EnrichHeadToHeadV2(confStandings, confGames);
            EnrichSOS(confStandings);

            return confStandings
                .Where(s => !string.IsNullOrEmpty(s.Conference)
                         && s.Conference.ToUpper() != "IND")
                .GroupBy(s => s.Conference)
                .ToDictionary(g => g.Key, g => g.ToList());
        }


        /// <summary>
        /// V2: Builds projected conference standings combining actual results through
        /// throughWeek with projections for remaining games.
        /// Legacy equivalent: BuildProjectedConferenceStandingsAsync().
        /// </summary>
        private async Task<Dictionary<string, List<ConferenceStanding>>> BuildProjectedConferenceStandingsV2Async(
            int year, int? throughWeek, CancellationToken token)
        {
            var Teams    = await _uow.Teams.GetDictionaryByTeamIdAsync(token);
            var confLookup = await _uow.Conferences.GetDictionaryAsync(token);
            var confByYear = await _uow.TeamsConferenceHistory.GetConferenceIdsByYearAsync(year, token);
            var records = throughWeek.HasValue ? await _uow.WeeklyRankings.GetByYearAndWeekAsync(year, (int)throughWeek, token) :
                                                 await _uow.WeeklyRankings.GetByYearAsync(year, token);
            var recordById = records.ToDictionary(tr => tr.TeamID);

            var fbsTeamsThisYear = Teams.Values
                .Where(t => string.Equals(t.Division, "fbs", StringComparison.OrdinalIgnoreCase)
                         && confByYear.ContainsKey(t.TeamId))
                .ToList();

            var allGames = await _uow.Games.GetByYearAsync(year, token);

            // Exclude championship-week conference games from the pool of
            // "remaining regular season" games to project — standings decide
            // who plays in the championship, so the championship game itself
            // can't also be projected as a normal remaining conference game.
            // Previously used the year's max Games.Week as a proxy for this,
            // which broke as soon as bowls/playoffs (Week 20+) landed in the
            // table for that year — see ChampionshipWeekCalculator remarks.
            var championshipWeek = ChampionshipWeekCalculator.GetChampionshipWeek(year, allGames);
            if (championshipWeek.HasValue)
            {
                allGames = allGames.Where(g => g.Week < championshipWeek.Value).ToList();
            }

            var confGames = allGames.Where(g => g.ConferenceGame == true).ToList();

            bool IsPlayed(Games g) =>
                ((g.HomePoints ?? 0) > 0 || (g.AwayPoints ?? 0) > 0) &&
                (!throughWeek.HasValue || g.Week <= throughWeek.Value);

            var playedConfGames   = confGames.Where(IsPlayed).ToList();
            var unplayedConfGames = confGames.Where(g => !IsPlayed(g)).ToList();

            var allProjections = await _projectionCache.GetAllProjections(year, token);

            var projectedResults = unplayedConfGames.Select(g =>
            {
                if (allProjections.TryGetValue(g.GameId, out var pred))
                {
                    bool homeWins = pred.IsTeamProjectedWinner;
                    return homeWins
                        ? (WinnerId: g.HomeId, LoserId: g.AwayId, GameId: g.GameId)
                        : (WinnerId: g.AwayId, LoserId: g.HomeId, GameId: g.GameId);
                }
                return (WinnerId: g.HomeId, LoserId: g.AwayId, GameId: g.GameId);
            }).ToList();

            var overallRankByTeam = await GetOverallRankLookupAsync(year, throughWeek, token);

            var confStandings = fbsTeamsThisYear.Select(t =>
            {
                confByYear.TryGetValue(t.TeamId, out var confId);
                confLookup.TryGetValue(confId, out var conf);
                var confAbbr = conf?.Abbreviation ?? string.Empty;

                int actualWins = 0, actualLosses = 0, ptsFor = 0, ptsAgainst = 0;
                var gameScores = new List<ConferenceGameScore>();
                foreach (var g in playedConfGames.Where(g => g.HomeId == t.TeamId || g.AwayId == t.TeamId))
                {
                    bool isHome  = g.HomeId == t.TeamId;
                    int myPts    = isHome ? (g.HomePoints ?? 0) : (g.AwayPoints ?? 0);
                    int oppPts   = isHome ? (g.AwayPoints ?? 0) : (g.HomePoints ?? 0);
                    if (myPts > oppPts) actualWins++; else actualLosses++;
                    ptsFor     += myPts;
                    ptsAgainst += oppPts;

                    var oppId = isHome ? g.AwayId : g.HomeId;
                    if (oppId.HasValue)
                        gameScores.Add(new ConferenceGameScore
                        {
                            OpponentId    = oppId.Value,
                            PointsFor     = myPts,
                            PointsAgainst = oppPts
                        });
                }

                var projWins   = projectedResults.Count(r => r.WinnerId == t.TeamId &&
                                     unplayedConfGames.Any(g => g.GameId == r.GameId));
                var projLosses = projectedResults.Count(r => r.LoserId  == t.TeamId &&
                                     unplayedConfGames.Any(g => g.GameId == r.GameId));

                recordById.TryGetValue(t.TeamId, out var rec);
                var overallRank = overallRankByTeam.TryGetValue(t.TeamId, out var orank) ? orank : (int?)null;

                return new ConferenceStanding
                {
                    TeamId            = t.TeamId,
                    TeamName          = t.TeamName,
                    Conference        = confAbbr,
                    Division          = RatingCalculator.GetDivision(t.TeamName, confAbbr),
                    ConferenceWins = actualWins + projWins,
                    ConferenceLosses = actualLosses + projLosses,
                    ActualConferenceWins = actualWins,
                    ActualConferenceLosses = actualLosses,
                    OverallWins = rec != null ? (int)rec.Wins   : 0,
                    OverallLosses     = rec != null ? (int)rec.Losses : 0,
                    ConfPointsFor     = ptsFor,
                    ConfPointsAgainst = ptsAgainst,
                    ConferenceGameScores = gameScores,
                    InternalRatingScore  = overallRank
                };
            }).ToList();

            foreach (var standing in confStandings)
            {
                var playedH2H = playedConfGames
                    .Where(g => g.HomeId == standing.TeamId || g.AwayId == standing.TeamId)
                    .GroupBy(g => (int)(g.HomeId == standing.TeamId ? g.AwayId : g.HomeId)!)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Count(game =>
                            (game.HomeId == standing.TeamId && (game.HomePoints ?? 0) > (game.AwayPoints ?? 0)) ||
                            (game.AwayId == standing.TeamId && (game.AwayPoints ?? 0) > (game.HomePoints ?? 0))) >
                             g.Count(game =>
                            (game.HomeId == standing.TeamId && (game.HomePoints ?? 0) < (game.AwayPoints ?? 0)) ||
                            (game.AwayId == standing.TeamId && (game.AwayPoints ?? 0) < (game.HomePoints ?? 0))));

                var projH2H = projectedResults
                    .Where(r => (r.WinnerId == standing.TeamId || r.LoserId == standing.TeamId) &&
                                !playedH2H.ContainsKey((int)(r.WinnerId == standing.TeamId ? r.LoserId : r.WinnerId)!))
                    .GroupBy(r => (int)(r.WinnerId == standing.TeamId ? r.LoserId : r.WinnerId)!)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Count(r => r.WinnerId == standing.TeamId) >
                             g.Count(r => r.LoserId  == standing.TeamId));

                standing.HeadToHeadResults = playedH2H.Concat(projH2H)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }

            EnrichSOS(confStandings);

            return confStandings
                .Where(s => !string.IsNullOrEmpty(s.Conference)
                         && s.Conference.ToUpper() != "IND")
                .GroupBy(s => s.Conference)
                .ToDictionary(g => g.Key, g => g.ToList());
        }


        // ── Power Rankings ───────────────────────────────────────────────────────

        /// <summary>
        /// Returns each team's OverallRank (1 = best) for the given year/week
        /// — the same ordinal GetPowerRankingsV2Async assigns and displays as
        /// "Rank" in the Rankings UI — without paying for the full
        /// PowerRankingRowResponse shape (trend history, offensive/defensive
        /// z-scores, etc.) that a tiebreaker calculation doesn't need.
        ///
        /// Deliberately duplicates GetPowerRankingsV2Async's sort key
        /// (composite actual+projected win% → CombinedSOS → RosterRank) rather
        /// than refactoring that method to share this directly — GetPowerRankingsV2Async
        /// is already a large method with two branches (throughWeek vs. not);
        /// this keeps the extraction low-risk to the existing, live endpoint.
        /// If the ranking sort key changes, both places need to change —
        /// worth revisiting as a shared refactor once this has proven out.
        ///
        /// Feeds ConferenceStanding.InternalRatingScore in
        /// BuildConferenceStandingsV2Async / BuildProjectedConferenceStandingsV2Async,
        /// which in turn replaces every external-ranking tiebreaker step
        /// (SportSource, CFP/AP polls, computer composites) with the app's own
        /// algorithmic rank.
        /// </summary>
        private async Task<Dictionary<int, int>> GetOverallRankLookupAsync(
            int year, int? throughWeek, CancellationToken token)
        {
            var Teams = await _uow.Teams.GetDictionaryByTeamIdAsync(token);

            var weekly = throughWeek.HasValue
                ? await _uow.WeeklyRankings.GetByYearAndWeekAsync(year, Math.Max(0, throughWeek.Value), token)
                : await _uow.WeeklyRankings.GetByYearAsync(year, token);

            if (!weekly.Any())
                return new Dictionary<int, int>();

            var currentRecords = await _uow.TeamRecords.GetByYearAsync(year, token);
            var currentRecordLookup = currentRecords.ToDictionary(r => r.TeamID);

            var rosterRankByTeam = currentRecords
                .Where(r => r.ZRoster.HasValue)
                .OrderByDescending(r => r.ZRoster!.Value)
                .Select((r, i) => new { r.TeamID, Rank = i + 1 })
                .ToDictionary(x => x.TeamID, x => x.Rank);

            var games = await _uow.Games.GetByYearAsync(year, token);
            var projections = await _projectionCache.GetAllProjections(year, token);
            var projRecordByTeam = BuildProjectedRecordRollup(games, projections, throughWeek);
            var actualRecordByTeam = BuildActualRecordRollup(games, throughWeek);

            return weekly
                .Where(wr => wr.Ranking.HasValue)
                .Where(wr => currentRecordLookup.ContainsKey(wr.TeamID))
                .Select(wr =>
                {
                    projRecordByTeam.TryGetValue(wr.TeamID, out var projWL);
                    actualRecordByTeam.TryGetValue(wr.TeamID, out var actualWL);
                    var rosterRank = rosterRankByTeam.TryGetValue(wr.TeamID, out var rr) ? rr : (int?)null;

                    var compositeWins = actualWL.Wins + projWL.Wins;
                    var compositeLosses = actualWL.Losses + projWL.Losses;
                    var compositeTotal = compositeWins + compositeLosses;

                    return new
                    {
                        wr.TeamID,
                        wr.Ranking,
                        RosterRank = rosterRank,
                        CombinedSOS = wr.CombinedSOS,
                        CompositeWinPct = compositeTotal > 0
                            ? (double)compositeWins / compositeTotal
                            : 0.0
                    };
                })
                .OrderByDescending(x => x.Ranking)
                .ThenByDescending(x => (double?)x.CombinedSOS ?? double.MinValue)
                .ThenBy(x => x.RosterRank ?? int.MaxValue)
                .Select((x, i) => new { x.TeamID, Rank = i + 1 })
                .ToDictionary(x => x.TeamID, x => x.Rank);
        }


        // ── Shared helpers (used by V2 partial) ──────────────────────────────────

        internal IReadOnlyList<object> BuildQualifierResponse(
            Dictionary<string, List<ConferenceStanding>> standingsByConference,
            ConferenceChampionshipService service,
            bool includeContenders, int? throughWeek = null)
        {
            return standingsByConference
                .Where(kvp => kvp.Value.Count >= 2)
                .Select(kvp => service.GetQualifiers(kvp.Key, kvp.Value))
                .Where(r => r.Qualifier1 != null && r.Qualifier2 != null)
                .OrderBy(r => RatingCalculator.ConferenceDisplayOrder(r.Conference))
                .Select(r =>
                {
                    var q1 = new { r.Qualifier1.TeamName, r.Qualifier1.ConferenceWins, r.Qualifier1.ConferenceLosses, r.Qualifier1.ActualConferenceWins, r.Qualifier1.ActualConferenceLosses, r.Qualifier1.OverallWins, r.Qualifier1.OverallLosses, r.Qualifier1.Division };
                    var q2 = new { r.Qualifier2.TeamName, r.Qualifier2.ConferenceWins, r.Qualifier2.ConferenceLosses, r.Qualifier2.ActualConferenceWins, r.Qualifier2.ActualConferenceLosses, r.Qualifier2.OverallWins, r.Qualifier2.OverallLosses, r.Qualifier2.Division };

                    if (includeContenders)
                        return (object)new
                        {
                            r.Conference, r.Format, Qualifier1 = q1, Qualifier2 = q2,
                            Contenders = r.Contenders.Select(c => new { c.TeamName, c.ConferenceWins, c.ConferenceLosses, c.ConferenceRecord,c.OverallWins,c.OverallLosses, c.OverallRecord, c.ActualConferenceWins, c.ActualConferenceLosses, c.ActualConferenceRecord }).ToList(),
                            r.Qualifier1Method, r.Qualifier2Method, r.TiebreakerLog, r.StubsApplied,
                            SimulatedThrough = throughWeek.HasValue
                                ? $"Week {throughWeek} (weeks {throughWeek + 1}-15 projected)"
                                : "Full season actual results"
                        };

                    return (object)new
                    {
                        r.Conference, r.Format, Qualifier1 = q1, Qualifier2 = q2,
                        r.Qualifier1Method, r.Qualifier2Method, r.TiebreakerLog, r.StubsApplied
                    };
                }).ToList();
        }


        // ════════════════════════════════════════════════════════════════════════
        // Helper — builds a single game object in the same shape as
        // GetScheduleV2Async's results. Used for the championship title game.
        // Once the schedule method itself is refactored to call this helper,
        // the duplication goes away.
        // ════════════════════════════════════════════════════════════════════════

        private object BuildTitleGameObject(
            Games g,
            IReadOnlyDictionary<int, Teams> teams,
            IReadOnlyDictionary<int, GamePrediction> allProjections,
            Dictionary<int, Dictionary<int, WeeklyRanking>> rankingsByWeek,
            Dictionary<int, List<Lines>> linesByGameId,
            Func<int, int> lookupWeek,
            Func<Teams?, string> getConfAbbr,
            Func<Teams?, string> getTier,
            object contenders,
            List<string> tiebreakerLog)
        {
            teams.TryGetValue(g.HomeId ?? 0, out var homeTeam);
            teams.TryGetValue(g.AwayId ?? 0, out var awayTeam);

            var homePoints = g.HomePoints ?? 0;
            var awayPoints = g.AwayPoints ?? 0;
            var isPlayed = homePoints > 0 || awayPoints > 0;
            var actualOU = homePoints + awayPoints;
            char location = g.NeutralSite == true ? 'N' : 'H';

            double? projHome = null, projAway = null;
            if (allProjections.TryGetValue(g.GameId, out var pred))
            {
                projHome = Math.Max(0, Math.Round(pred.PredictedTeamScore, 1));
                projAway = Math.Max(0, Math.Round(pred.PredictedOpponentScore, 1));
            }

            var projOU = projHome.HasValue && projAway.HasValue
                         ? (double?)Math.Round(projHome.Value + projAway.Value, 1) : null;

            // ── Team stats ────────────────────────────────────────────────────
            var snapshotWeek = lookupWeek(g.Week);
            rankingsByWeek.TryGetValue(snapshotWeek, out var snapshot);

            object? homeStats = null;
            object? awayStats = null;
            bool isWeek1 = g.Week == 1;

            if (snapshot != null)
            {
                if (snapshot.TryGetValue(g.HomeId ?? 0, out var hwr))
                    homeStats = new
                    {
                        TeamId = hwr.TeamID,
                        TeamName = homeTeam?.TeamName ?? g.HomeName,
                        OverallRank = isWeek1 ? 0 : hwr.OverallRank,
                        Record = isWeek1 ? "0-0" : $"{hwr.Wins}-{hwr.Losses}",
                        PowerRating = isWeek1 ? (double?)null : (double?)hwr.Ranking,
                        CombinedSOS = isWeek1 ? (double?)null : (double?)hwr.CombinedSOS,
                        OffensiveRank = isWeek1 ? (int?)null : hwr.OffensiveRank,
                        AvgPointsScored = isWeek1 ? (double?)null : (double?)hwr.AvgPointsScored,
                        OffensiveZScore = isWeek1 ? (double?)null : (double?)hwr.OffensiveZScore,
                        DefensiveRank = isWeek1 ? (int?)null : hwr.DefensiveRank,
                        AvgPointsAllowed = isWeek1 ? (double?)null : (double?)hwr.AvgPointsAllowed,
                        DefensiveZScore = isWeek1 ? (double?)null : (double?)hwr.DefensiveZScore,
                    };

                if (snapshot.TryGetValue(g.AwayId ?? 0, out var awr))
                    awayStats = new
                    {
                        TeamId = awr.TeamID,
                        TeamName = awayTeam?.TeamName ?? g.AwayName,
                        OverallRank = isWeek1 ? 0 : awr.OverallRank,
                        Record = isWeek1 ? "0-0" : $"{awr.Wins}-{awr.Losses}",
                        PowerRating = isWeek1 ? (double?)null : (double?)awr.Ranking,
                        CombinedSOS = isWeek1 ? (double?)null : (double?)awr.CombinedSOS,
                        OffensiveRank = isWeek1 ? (int?)null : awr.OffensiveRank,
                        AvgPointsScored = isWeek1 ? (double?)null : (double?)awr.AvgPointsScored,
                        OffensiveZScore = isWeek1 ? (double?)null : (double?)awr.OffensiveZScore,
                        DefensiveRank = isWeek1 ? (int?)null : awr.DefensiveRank,
                        AvgPointsAllowed = isWeek1 ? (double?)null : (double?)awr.AvgPointsAllowed,
                        DefensiveZScore = isWeek1 ? (double?)null : (double?)awr.DefensiveZScore,
                    };
            }

            // ── Vegas lines — average across providers ────────────────────────
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

            // Shape matches GetScheduleV2Async exactly so the client deserialises
            // into the same GameResult model with no special handling.
            return new
            {
                Id = g.GameId,
                g.Year,
                g.Week,
                GameDate = g.GameDate,
                GameDay = g.GameDay,
                HomeName = g.HomeName,
                HomeId = g.HomeId,
                HomeConf = getConfAbbr(homeTeam),
                HomeTier = getTier(homeTeam),
                HomePoints = homePoints,
                HomeProjScore = projHome,
                AwayName = g.AwayName,
                AwayId = g.AwayId,
                AwayConf = getConfAbbr(awayTeam),
                AwayTier = getTier(awayTeam),
                AwayPoints = awayPoints,
                AwayProjScore = projAway,
                Location = location,
                IsPlayed = isPlayed,
                ActualOU = actualOU,
                ProjOU = projOU,
                SeasonType = g.SeasonType,
                HomeStats = homeStats,
                AwayStats = awayStats,
                VegasLines = vegasLines,

                // Legacy fields — mirrors GetScheduleV2Async for binding consistency
                WinnerName = homePoints >= awayPoints ? g.HomeName : g.AwayName,
                WinnerShortName = homePoints >= awayPoints ? g.HomeName : g.AwayName,
                WinnerId = homePoints >= awayPoints ? g.HomeId : g.AwayId,
                WinnerConf = homePoints >= awayPoints ? getConfAbbr(homeTeam) : getConfAbbr(awayTeam),
                WPoints = homePoints >= awayPoints ? homePoints : awayPoints,
                LoserName = homePoints >= awayPoints ? g.AwayName : g.HomeName,
                LoserShortName = homePoints >= awayPoints ? g.AwayName : g.HomeName,
                LoserId = homePoints >= awayPoints ? g.AwayId : g.HomeId,

                Contenders = contenders,
                TiebreakerLog = tiebreakerLog,
            };
        }



        internal static void EnrichSOS(List<ConferenceStanding> standings)
        {
            // NOTE: this used to also set standing.CommonOpponentWinPct here,
            // computed as each team's win% across its entire HeadToHeadResults
            // dictionary — i.e. the same number as ConferenceWinPct. Since the
            // tiebreaker engine only ever compares that field within a group
            // already tied ON ConferenceWinPct, it could never separate
            // anyone; the "common opponents" tiebreaker step was mathematically
            // inert in every conference that reached it. That field has been
            // removed from ConferenceStanding — "common opponents" win% is now
            // computed live, per currently-tied pool, by CommonOpponentsStep,
            // since the correct intersection of common opponents changes
            // depending on who's currently tied and can't be precomputed
            // per-team in isolation.

            var recordById = standings.ToDictionary(r => r.TeamId);
            foreach (var standing in standings)
            {
                var oppWinPcts = standing.HeadToHeadResults.Keys
                    .Where(id => recordById.ContainsKey(id))
                    .Select(id => recordById[id].ConferenceWinPct)
                    .ToList();
                standing.ConferenceOpponentWinPct = oppWinPcts.Any() ? oppWinPcts.Average() : 0.0;
            }
        }


        /// <summary>
        /// V2 version of EnrichHeadToHead — operates on Games (home/away) instead of Game (winner/loser).
        /// EnrichSOS is data-agnostic and shared between legacy and V2.
        /// </summary>
        private static void EnrichHeadToHeadV2(List<ConferenceStanding> standings, List<Games> confGames)
        {
            foreach (var standing in standings)
            {
                standing.HeadToHeadResults = confGames
                    .Where(g => g.HomeId == standing.TeamId || g.AwayId == standing.TeamId)
                    .GroupBy(g => (int)(g.HomeId == standing.TeamId ? g.AwayId : g.HomeId)!)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Count(game =>
                            (game.HomeId == standing.TeamId && (game.HomePoints ?? 0) > (game.AwayPoints ?? 0)) ||
                            (game.AwayId == standing.TeamId && (game.AwayPoints ?? 0) > (game.HomePoints ?? 0))) >
                             g.Count(game =>
                            (game.HomeId == standing.TeamId && (game.HomePoints ?? 0) < (game.AwayPoints ?? 0)) ||
                            (game.AwayId == standing.TeamId && (game.AwayPoints ?? 0) < (game.HomePoints ?? 0))));
            }
        }


        // ── Private helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Rolls up a projected win/loss total for every team appearing in
        /// <paramref name="games"/>: games already reflected in the caller's
        /// baseline are skipped; every remaining game gets a predicted W/L from
        /// the existing Projections snapshot (home team's score is always
        /// PredictedTeamScore, per the cache convention used throughout this
        /// file), added on top of the caller's real record.
        ///
        /// afterWeek == null  → baseline is "already played" (current/live view).
        /// afterWeek.HasValue → baseline is "Week &lt;= afterWeek" (historical
        ///                      weekly view) — later games are always projected,
        ///                      even if they've since actually been played, since
        ///                      this reconstructs what was knowable as of that week.
        ///
        /// Ties default to a home win via the same ">=" convention already used
        /// in BuildProjectedConferenceStandingsV2Async / GetProjectedStandingsV2Async.
        /// This is a known placeholder — see the Sandbox tie-breaker work.
        /// </summary>
        private static Dictionary<int, (int Wins, int Losses)> BuildProjectedRecordRollup(
            List<Games> games,
            Dictionary<int, GamePrediction> projections,
            int? afterWeek)
        {
            var result = new Dictionary<int, (int Wins, int Losses)>();

            void Add(int teamId, bool won)
            {
                result.TryGetValue(teamId, out var wl);
                result[teamId] = won ? (wl.Wins + 1, wl.Losses) : (wl.Wins, wl.Losses + 1);
            }

            foreach (var g in games)
            {
                if (!g.HomeId.HasValue || !g.AwayId.HasValue) continue;

                bool isPlayed = (g.HomePoints ?? 0) > 0 || (g.AwayPoints ?? 0) > 0;

                // "Already accounted for by Actual" requires BOTH that the game was
                // really played AND that it falls at/before the as-of cutoff. A game
                // at week <= afterWeek that was never actually played must NOT be
                // treated as baseline — it needs to fall through to projection below,
                // or it disappears from both Actual and Projected entirely.
                bool countedInBaseline = isPlayed && (!afterWeek.HasValue || g.Week <= afterWeek.Value);

                if (countedInBaseline) continue;

                bool homeWins = projections.TryGetValue(g.GameId, out var pred)
                    ? pred.IsTeamProjectedWinner
                    : true; // home default — matches GetProjectedStandingsV2Async's fallback

                Add(g.HomeId.Value, homeWins);
                Add(g.AwayId.Value, !homeWins);
            }

            return result;
        }


        /// <summary>
        /// Rolls up ACTUAL wins/losses only — a game counts here only if it has
        /// really been played (HomePoints/AwayPoints populated), unlike
        /// WeeklyRankings.Wins/Losses and TeamRecords.Wins/Losses, which both bake
        /// in projected results for unplayed games regardless of game status.
        ///
        /// throughWeek: when supplied, only played games at or before this week
        ///              count (matches the semantics of BuildProjectedRecordRollup's
        ///              afterWeek). When null, all played games in the season count.
        /// </summary>
        private static Dictionary<int, (int Wins, int Losses)> BuildActualRecordRollup(
            List<Games> games,
            int? throughWeek)
        {
            var result = new Dictionary<int, (int Wins, int Losses)>();

            void Add(int teamId, bool won)
            {
                result.TryGetValue(teamId, out var wl);
                result[teamId] = won ? (wl.Wins + 1, wl.Losses) : (wl.Wins, wl.Losses + 1);
            }

            foreach (var g in games)
            {
                if (!g.HomeId.HasValue || !g.AwayId.HasValue) continue;

                bool isPlayed = (g.HomePoints ?? 0) > 0 || (g.AwayPoints ?? 0) > 0;
                if (!isPlayed) continue;

                if (throughWeek.HasValue && g.Week > throughWeek.Value) continue;

                bool homeWon = (g.HomePoints ?? 0) > (g.AwayPoints ?? 0);
                Add(g.HomeId.Value, homeWon);
                Add(g.AwayId.Value, !homeWon);
            }

            return result;
        }
    }
}
