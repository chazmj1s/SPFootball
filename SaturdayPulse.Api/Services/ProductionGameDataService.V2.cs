using Microsoft.EntityFrameworkCore.Metadata.Internal;
using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.Models;
using SaturdayPulse.Utilities;
using SQLitePCL;
using System.Diagnostics;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// V2 methods for ProductionGameDataService — reads from the new CFBD-sourced tables:
    ///   Games, Teams, Conferences, Lines, TeamsConferenceHistory
    /// Legacy methods remain in ProductionGameDataService.cs untouched.
    /// When all V2 methods are validated, delete ProductionGameDataService.cs and rename this file.
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

            string GetConfAbbr(Teams? t)
            {
                if (t?.ConferenceId == null) return string.Empty;
                confLookup.TryGetValue(t.ConferenceId.Value, out var conf);
                return conf?.Abbreviation ?? string.Empty;
            }

            string GetConfName(Teams? t)
            {
                if (t?.ConferenceId == null) return string.Empty;
                confLookup.TryGetValue(t.ConferenceId.Value, out var conf);
                return conf?.Name ?? string.Empty;
            }

            var allProjections = await _projectionCache.GetAllProjections(targetYear, token);
            Console.WriteLine($"GetScheduleV2Async:{targetYear}: Projections - {allProjections.Count()}");

            var firstGame = games.FirstOrDefault();
            if (firstGame != null)
            {
                var found = allProjections.TryGetValue(firstGame.GameId, out _);
                Console.WriteLine($"First GameId: {firstGame.GameId}, Found: {found}, Cache sample: {string.Join(", ", allProjections.Keys.Take(5))}");
            }

            var results = games.Select(g =>
            {
                teams.TryGetValue(g.HomeId ?? 0, out var homeTeam);
                teams.TryGetValue(g.AwayId ?? 0, out var awayTeam);

                var homePoints = g.HomePoints ?? 0;
                var awayPoints = g.AwayPoints ?? 0;
                var isPlayed = homePoints > 0 || awayPoints > 0;
                var actualOU = homePoints + awayPoints;

                char location = g.NeutralSite == true ? 'N' : 'H'; // H = home team is identified; away team is away

                double? projHome = null, projAway = null;
                if (allProjections.TryGetValue(g.GameId, out var pred))
                {
                    // Projections are stored as PredictedTeamScore = home, PredictedOpponentScore = away
                    projHome = Math.Max(0, Math.Round(pred.PredictedTeamScore, 1));
                    projAway = Math.Max(0, Math.Round(pred.PredictedOpponentScore, 1));
                }

                var projOU = projHome.HasValue && projAway.HasValue
                             ? (double?)Math.Round(projHome.Value + projAway.Value, 1) : null;

                return (object)new
                {
                    Id = g.GameId,
                    g.Year,
                    g.Week,
                    GameDate = g.GameDate,
                    GameDay = g.GameDay,

                    // Home team (bottom row on client)
                    HomeName = g.HomeName,
                    HomeId = g.HomeId,
                    HomeConf = GetConfAbbr(homeTeam),
                    HomeTier = RatingCalculator.GetConferenceTier(GetConfName(homeTeam), g.HomeName),
                    HomePoints = homePoints,
                    HomeProjScore = projHome,

                    // Away team (top row on client)
                    AwayName = g.AwayName,
                    AwayId = g.AwayId,
                    AwayConf = GetConfAbbr(awayTeam),
                    AwayTier = RatingCalculator.GetConferenceTier(GetConfName(awayTeam), g.AwayName),
                    AwayPoints = awayPoints,
                    AwayProjScore = projAway,

                    Location = location,
                    IsPlayed = isPlayed,
                    ActualOU = actualOU,
                    ProjOU = projOU,
                    SeasonType = g.SeasonType,

                    // Legacy winner/loser fields — kept temporarily so the existing client
                    // doesn't break before it's updated to consume the new home/away fields.
                    WinnerName = homePoints >= awayPoints ? g.HomeName : g.AwayName,
                    WinnerShortName = homePoints >= awayPoints ? g.HomeName : g.AwayName,
                    WinnerId = homePoints >= awayPoints ? g.HomeId : g.AwayId,
                    WinnerConf = homePoints >= awayPoints ? GetConfAbbr(homeTeam) : GetConfAbbr(awayTeam),
                    WinnerTier = homePoints >= awayPoints
                                      ? RatingCalculator.GetConferenceTier(GetConfName(homeTeam), g.HomeName)
                                      : RatingCalculator.GetConferenceTier(GetConfName(awayTeam), g.AwayName),
                    WPoints = homePoints >= awayPoints ? homePoints : awayPoints,
                    LoserName = homePoints >= awayPoints ? g.AwayName : g.HomeName,
                    LoserShortName = homePoints >= awayPoints ? g.AwayName : g.HomeName,
                    LoserId = homePoints >= awayPoints ? g.AwayId : g.HomeId,
                    LoserConf = homePoints >= awayPoints ? GetConfAbbr(awayTeam) : GetConfAbbr(homeTeam),
                    LoserTier = homePoints >= awayPoints
                                      ? RatingCalculator.GetConferenceTier(GetConfName(awayTeam), g.AwayName)
                                      : RatingCalculator.GetConferenceTier(GetConfName(homeTeam), g.HomeName),
                    LPoints = homePoints >= awayPoints ? awayPoints : homePoints,
                };
            }).ToList();

            return new ScheduleResult(results);
        }
        // ── Teams and Rivalries ──────────────────────────────────────────────────

        /// <summary>
        /// V2: reads rivalry game history from Games table (CFBD-sourced).
        /// Legacy equivalent: GetRivalryHistoryAsync() which reads from Game table.
        ///
        /// Team lookup via Teams + Conferences instead of Team.
        /// Winner/loser derived from home/away points; home team defaults for unplayed.
        /// Projection and rivalry metadata (MatchupHistory, AvgScoreDeltas) unchanged —
        /// those tables are shared and will be rebuilt via Developer backfill.
        /// </summary>
        public async Task<RivalryHistoryResult> GetRivalryHistoryV2Async(
            int team1Id, int team2Id, int years, CancellationToken token = default)
        {
            var Teams    = await _uow.Teams.GetDictionaryByTeamIdAsync(token);
            var confLookup = await _uow.Conferences.GetDictionaryAsync(token);

            if (!Teams.TryGetValue(team1Id, out var team1))
                throw new KeyNotFoundException($"Team {team1Id} not found.");
            if (!Teams.TryGetValue(team2Id, out var team2))
                throw new KeyNotFoundException($"Team {team2Id} not found.");

            string ConfAbbr(Teams? t)
            {
                if (t?.ConferenceId == null) return string.Empty;
                confLookup.TryGetValue(t.ConferenceId.Value, out var conf);
                return conf?.Abbreviation ?? string.Empty;
            }

            var cutoffYear     = DateTime.Now.Year - years;
            var games          = await _uow.Games.GetRivalryHistoryAsync(team1Id, team2Id, cutoffYear, token);
            var rivalry        = await _uow.Lookups.GetMatchupHistoryAsync(team1Id, team2Id, token);
            var avgScoreDeltas = await _uow.Lookups.GetAvgScoreDeltasAsync(token);

            var avgTeamScore = games.Count > 0
                ? (games.Average(g => g.HomePoints ?? 0) + games.Average(g => g.AwayPoints ?? 0)) / 2.0
                : 28.0;

            var history = games
                .Where(g => (g.HomePoints ?? 0) > 0 || (g.AwayPoints ?? 0) > 0)
                .Select(g =>
                {
                    bool team1IsHome = g.HomeId == team1Id;
                    var team1Score   = team1IsHome ? (g.HomePoints ?? 0) : (g.AwayPoints ?? 0);
                    var team2Score   = team1IsHome ? (g.AwayPoints ?? 0) : (g.HomePoints ?? 0);
                    bool team1Won    = team1Score > team2Score;
                    char location    = g.NeutralSite == true ? 'N' : team1IsHome ? 'H' : 'A';

                    return (object)new
                    {
                        g.Year,
                        g.Week,
                        Location   = location,
                        Team1Score = team1Score,
                        Team2Score = team2Score,
                        Margin     = team1Score - team2Score,
                        ActualOU   = team1Score + team2Score,
                        Team1Won   = team1Won,
                        WinnerName = team1Won ? team1.TeamName : team2.TeamName,
                        Score      = $"{Math.Max(team1Score, team2Score)}-{Math.Min(team1Score, team2Score)}"
                    };
                }).ToList();

            object? projection = null;
            var currentYear = (short)DateTime.Now.Year;
            var t1Record    = await _uow.TeamRecords.GetByTeamAndYearAsync(team1Id, currentYear, token);
            var t2Record    = await _uow.TeamRecords.GetByTeamAndYearAsync(team2Id, currentYear, token);

            if (t1Record != null && t2Record != null)
            {
                var t1WinPct    = RatingCalculator.BucketWinPct(t1Record.Wins, t1Record.Wins + t1Record.Losses);
                var t2WinPct    = RatingCalculator.BucketWinPct(t2Record.Wins, t2Record.Wins + t2Record.Losses);
                var maxPct      = Math.Max(t1WinPct, t2WinPct);
                var minPct      = Math.Min(t1WinPct, t2WinPct);
                var asd         = avgScoreDeltas.FirstOrDefault(a => a.Team1WinPct == maxPct && a.Team2WinPct == minPct);
                var delta       = asd != null && asd.SampleSize >= 10
                    ? Math.Max(-35.0, Math.Min(35.0, (double)asd.AverageScoreDelta)) : AvgScoreDelta.DefaultAverageScoreDelta;
                var deltaFromT1 = RatingCalculator.ExpectedFromPerspective(delta, t1WinPct, t2WinPct);
                if (t1Record.Ranking.HasValue && t2Record.Ranking.HasValue)
                    deltaFromT1 += (double)(t1Record.Ranking.Value - t2Record.Ranking.Value) * 0.15;

                var projT1 = Math.Round(avgTeamScore + deltaFromT1 / 2.0, 1);
                var projT2 = Math.Round(avgTeamScore - deltaFromT1 / 2.0, 1);

                projection = new
                {
                    Year           = currentYear,
                    ProjTeam1Score = projT1,
                    ProjTeam2Score = projT2,
                    ProjMargin     = Math.Round(projT1 - projT2, 1),
                    ProjOU         = Math.Round(projT1 + projT2, 1),
                    IsProjected    = true
                };
            }

            return new RivalryHistoryResult(
                team1Id, team1.TeamName, team1.Abbreviation ?? team1.TeamName,
                team2Id, team2.TeamName, team2.Abbreviation ?? team2.TeamName,
                rivalry?.RivalryName, rivalry?.RivalryTier,
                rivalry?.GamesPlayed ?? history.Count,
                rivalry?.AvgMargin, rivalry?.UpsetRate,
                history, projection);
        }

        // ── Teams ────────────────────────────────────────────────────────────────

        /// <summary>
        /// V2: reads from Teams + Conferences tables (CFBD-sourced).
        /// Legacy equivalent: GetTeamsAsync() which reads from Team table.
        ///
        /// Key differences from legacy:
        ///   - Conference name/abbr resolved via ConferenceId FK, not embedded in Team row
        ///   - ShortName not available from CFBD /teams endpoint; falls back to Abbreviation
        ///   - TeamId (int) replaces TeamID (int) — same value, different property name
        /// </summary>
        public async Task<TeamsResult> GetTeamsV2Async(CancellationToken token = default)
        {
            var teams      = await _uow.Teams.GetAllAsync(token);
            var confLookup = await _uow.Conferences.GetDictionaryAsync(token);

            var result = teams
                .Where(t => string.Equals(t.Division, "fbs", StringComparison.OrdinalIgnoreCase))
                .Select(t =>
                {
                    confLookup.TryGetValue(t.ConferenceId ?? -1, out var conf);

                    return (object)new
                    {
                        TeamID         = t.TeamId,
                        t.TeamName,
                        ShortName      = t.Abbreviation ?? t.TeamName,
                        Conference     = conf?.Name ?? string.Empty,
                        ConferenceAbbr = conf?.Abbreviation ?? string.Empty,
                        Division       = t.Division?.ToUpperInvariant(),
                        Tier           = RatingCalculator.GetConferenceTier(conf?.Name, t.TeamName)
                    };
                })
                .ToList();

            return new TeamsResult(result);
        }

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
        /// </summary>
        public async Task<ChampionshipQualifiersResult> GetProjectedChampionshipQualifiersV2Async(
            int? year, int? throughWeek, CancellationToken token = default)
        {
            var targetYear            = year ?? DateTime.Now.Year;
            var standingsByConference = await BuildProjectedConferenceStandingsV2Async(targetYear, throughWeek, token);
            var service               = new ConferenceChampionshipService();
            var results               = BuildQualifierResponse(standingsByConference, service, includeContenders: true, throughWeek: throughWeek);
            return new ChampionshipQualifiersResult(results);
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
                            projWin      = projMyScore >= projOppScore;
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

            var confStandings = fbsTeamsThisYear.Select(t =>
            {
                confByYear.TryGetValue(t.TeamId, out var confId);
                confLookup.TryGetValue(confId, out var conf);
                var confAbbr = conf?.Abbreviation ?? string.Empty;

                var teamConfGames = confGames
                    .Where(g => g.HomeId == t.TeamId || g.AwayId == t.TeamId)
                    .ToList();

                int confWins = 0, confLosses = 0, ptsFor = 0, ptsAgainst = 0;
                foreach (var g in teamConfGames)
                {
                    bool isHome  = g.HomeId == t.TeamId;
                    int myPts    = isHome ? (g.HomePoints ?? 0) : (g.AwayPoints ?? 0);
                    int oppPts   = isHome ? (g.AwayPoints ?? 0) : (g.HomePoints ?? 0);
                    bool won     = myPts > oppPts || (myPts == 0 && oppPts == 0 && isHome);
                    if (won) confWins++; else confLosses++;
                    ptsFor     += myPts;
                    ptsAgainst += oppPts;
                }

                recordById.TryGetValue(t.TeamId, out var rec);

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
                    ConfPointsAgainst      = ptsAgainst
                };
            }).ToList();

            EnrichHeadToHeadV2(confStandings, confGames);
            EnrichSOS(confStandings);

            return confStandings
                .Where(s => !string.IsNullOrEmpty(s.Conference)
                         && s.Conference != "IND"
                         && s.Conference != "Pac-12")
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

            if (allGames.Any())
            {
                var maxWeek = allGames.Max(g => g.Week);
                allGames = allGames.Where(g => g.Week < maxWeek).ToList();
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
                    bool homeWins = pred.PredictedTeamScore >= pred.PredictedOpponentScore;
                    return homeWins
                        ? (WinnerId: g.HomeId, LoserId: g.AwayId, GameId: g.GameId)
                        : (WinnerId: g.AwayId, LoserId: g.HomeId, GameId: g.GameId);
                }
                return (WinnerId: g.HomeId, LoserId: g.AwayId, GameId: g.GameId);
            }).ToList();

            var confStandings = fbsTeamsThisYear.Select(t =>
            {
                confByYear.TryGetValue(t.TeamId, out var confId);
                confLookup.TryGetValue(confId, out var conf);
                var confAbbr = conf?.Abbreviation ?? string.Empty;

                int actualWins = 0, actualLosses = 0, ptsFor = 0, ptsAgainst = 0;
                foreach (var g in playedConfGames.Where(g => g.HomeId == t.TeamId || g.AwayId == t.TeamId))
                {
                    bool isHome  = g.HomeId == t.TeamId;
                    int myPts    = isHome ? (g.HomePoints ?? 0) : (g.AwayPoints ?? 0);
                    int oppPts   = isHome ? (g.AwayPoints ?? 0) : (g.HomePoints ?? 0);
                    if (myPts > oppPts) actualWins++; else actualLosses++;
                    ptsFor     += myPts;
                    ptsAgainst += oppPts;
                }

                var projWins   = projectedResults.Count(r => r.WinnerId == t.TeamId &&
                                     unplayedConfGames.Any(g => g.GameId == r.GameId));
                var projLosses = projectedResults.Count(r => r.LoserId  == t.TeamId &&
                                     unplayedConfGames.Any(g => g.GameId == r.GameId));

                recordById.TryGetValue(t.TeamId, out var rec);

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
                    ConfPointsAgainst = ptsAgainst
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
                         && s.Conference != "IND"
                         && s.Conference != "Pac-12")
                .GroupBy(s => s.Conference)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        // ── Power Rankings ───────────────────────────────────────────────────────

        /// <summary>
        /// V2: reads team metadata from Teams + Conferences instead of Team.
        /// WeeklyRankings and TeamRecords are shared tables — unchanged.
        /// </summary>
        public async Task<PowerRankingsResult> GetPowerRankingsV2Async(
            int? year, int? throughWeek, CancellationToken token = default)
        {
            try
            {
                var targetYear = year ?? DateTime.Now.Year;
                var Teams = await _uow.Teams.GetDictionaryByTeamIdAsync(token);
                var confLookup = await _uow.Conferences.GetDictionaryAsync(token);

                string ConfName(Teams? t)
                {
                    if (t?.ConferenceId == null) return string.Empty;
                    confLookup.TryGetValue(t.ConferenceId.Value, out var conf);
                    return conf?.Name ?? string.Empty;
                }

                string ConfAbbr(Teams? t)
                {
                    if (t?.ConferenceId == null) return string.Empty;
                    confLookup.TryGetValue(t.ConferenceId.Value, out var conf);
                    return conf?.Abbreviation ?? string.Empty;
                }

                if (throughWeek.HasValue)
                {
                    var weekly = await _uow.WeeklyRankings.GetByYearAndWeekAsync(
                        targetYear, throughWeek.Value, token);

                    if (!weekly.Any())
                        throw new KeyNotFoundException(
                            $"No weekly rankings found for year {targetYear} week {throughWeek}.");

                    var currentRecords = await _uow.TeamRecords.GetByYearAsync(targetYear, token);
                    var currentRecordLookup = currentRecords.ToDictionary(r => r.TeamID);
                    var historicalRecords = await _uow.TeamRecords.GetHistoricalAsync(
                        targetYear - 10, targetYear, token);
                    var historyByTeam = historicalRecords
                        .GroupBy(tr => tr.TeamID)
                        .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Year).ToList());

                    var sample = currentRecords.FirstOrDefault();
                    Debug.WriteLine($"Sample TrendRating: {sample?.TrendRating}, Pedigree: {sample?.PedigreeRating}, Seed: {sample?.SeedRating}");

                    var result = weekly
                        .Where(wr => wr.Ranking.HasValue)
                        .Where(wr => currentRecordLookup.ContainsKey(wr.TeamID))
                        .OrderBy(wr => wr.OverallRank)
                        .Select(wr =>
                        {
                            Teams.TryGetValue(wr.TeamID, out var t);
                            currentRecordLookup.TryGetValue(wr.TeamID, out var currentRecord);
                            historyByTeam.TryGetValue(wr.TeamID, out var history);
                            history ??= [];

                            var confName = ConfName(t);
                            var confAbbr = ConfAbbr(t);

                            return new PowerRankingRowResponse
                            {
                                TeamID = wr.TeamID,
                                TeamName = t?.TeamName,
                                Conference = confName,
                                ConferenceAbbr = confAbbr,
                                Division = t?.Division,
                                Tier = RatingCalculator.GetConferenceTier(confName, t?.TeamName),
                                OverallRank = wr.OverallRank,
                                TierRank = wr.TierRank,
                                Ranking = (double?)wr.Ranking,
                                PowerRating = (double?)wr.PowerRating,
                                Year = (int)wr.Year,
                                Wins = wr.Wins,
                                Losses = wr.Losses,
                                BaseSOS = (double?)wr.BaseSOS,
                                CombinedSOS = (double?)wr.CombinedSOS,
                                AvgPointsScored = (double?)wr.AvgPointsScored,
                                AvgPointsAllowed = (double?)wr.AvgPointsAllowed,
                                OffensiveZScore = (double?)wr.OffensiveZScore,
                                DefensiveZScore = (double?)wr.DefensiveZScore,
                                OffensiveRank = wr.OffensiveRank,
                                DefensiveRank = wr.DefensiveRank,
                                TrendRating = (double?)(currentRecord?.TrendRating),
                                PedigreeRating = (double?)currentRecord?.PedigreeRating,
                                SeedRating = (double?)currentRecord?.SeedRating,
                                TrendHistory = history
                                    .Select(h => (double)(h.TrendRating ?? 0m)).ToList(),
                                PedigreeHistory = history
                                    .Select(h => (double)(h.PedigreeRating ?? 0m)).ToList()
                            };
                        }).ToList();

                    var sample2 = result.FirstOrDefault();
                    Debug.WriteLine($"Response TrendRating: {sample2?.TrendRating}, Pedigree: {sample2?.PedigreeRating}, Seed: {sample2?.SeedRating}");

                    return new PowerRankingsResult(true, result);
                }
                else
                {
                    var teamRecords = await _uow.TeamRecords.GetByYearWithTeamsAsync(targetYear, token);
                    var ranked = teamRecords.Where(tr => tr.Ranking.HasValue).ToList();

                    var withTiers = ranked
                        .Select(tr =>
                        {
                            Teams.TryGetValue(tr.TeamID, out var t);
                            var confName = ConfName(t);
                            var confAbbr = ConfAbbr(t);
                            return new
                            {
                                TeamRecord = tr,
                                Team = t,
                                ConfName = confName,
                                ConfAbbr = confAbbr,
                                Tier = RatingCalculator.GetConferenceTier(confName, t?.TeamName)
                            };
                        })
                        .OrderByDescending(t => t.TeamRecord.Ranking)
                        .ToList();

                    var withOverallRank = withTiers
                        .Select((t, i) => new { t.TeamRecord, t.Team, t.ConfName, t.ConfAbbr, t.Tier, OverallRank = i + 1 })
                        .ToList();

                    var tierRankLookup = new Dictionary<int, int>();
                    foreach (var tierGroup in withOverallRank.GroupBy(t => t.Tier))
                    {
                        var tieredTeams = tierGroup
                            .OrderByDescending(t => t.TeamRecord.Ranking)
                            .Select((t, i) => new { t.TeamRecord.TeamID, TierRank = i + 1 })
                            .ToList();
                        foreach (var team in tieredTeams)
                            tierRankLookup[team.TeamID] = team.TierRank;
                    }

                    var rankings = withOverallRank
                        .Select(t => new PowerRankingRowResponse
                        {
                            TeamID = t.TeamRecord.TeamID,
                            TeamName = t.Team?.TeamName ?? t.TeamRecord.Teams?.TeamName,
                            Conference = t.ConfName,
                            ConferenceAbbr = t.ConfAbbr,
                            Division = t.Team?.Division,
                            Tier = t.Tier,
                            OverallRank = t.OverallRank,
                            TierRank = tierRankLookup[t.TeamRecord.TeamID],
                            Ranking = (double?)t.TeamRecord.Ranking,
                            Year = t.TeamRecord.Year,
                            Wins = t.TeamRecord.Wins,
                            Losses = t.TeamRecord.Losses,
                            BaseSOS = (double?)t.TeamRecord.BaseSOS,
                            CombinedSOS = (double?)t.TeamRecord.CombinedSOS
                        }).ToList();

                    return new PowerRankingsResult(false, rankings);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
            }
                return new PowerRankingsResult(false, new List<PowerRankingRowResponse>());
            }

        public async Task<TeamSeasonArcResult> GetTeamSeasonArcAsync(
            int teamId, int year, CancellationToken token = default)
        {
            var team = await _uow.Teams.GetByTeamIdAsync(teamId, token)
                       ?? throw new KeyNotFoundException($"Team {teamId} not found.");

            var weeks = await _uow.WeeklyRankings.GetByTeamAndYearAsync(teamId, year, token);

            if (!weeks.Any())
                throw new KeyNotFoundException($"No weekly rankings found for team {teamId} in {year}.");

            var arc = weeks.Select(wr => (object)new
            {
                Week = (int)wr.Week,
                Ranking = (double?)wr.Ranking,
                CombinedSOS = (double?)wr.CombinedSOS,
                WinPct = wr.Wins + wr.Losses > 0
                  ? Math.Round((double)wr.Wins / (wr.Wins + wr.Losses), 3) : 0.0
            }).ToList();

            return new TeamSeasonArcResult(teamId, team.TeamName, year, arc);
        }

        // ── Rolling Averages ─────────────────────────────────────────────────────

        /// <summary>
        /// V2: team lookup via Teams + Conferences instead of Team.
        /// TeamRecords and RollingAverageService are shared — unchanged.
        /// </summary>
        public async Task<TeamRollingAveragesResult> GetTeamRollingAveragesV2Async(
            int teamId, int? startYear, CancellationToken token = default)
        {
            var Teams = await _uow.Teams.GetDictionaryByTeamIdAsync(token);
            if (!Teams.TryGetValue(teamId, out var team))
                throw new KeyNotFoundException($"Team {teamId} not found.");

            var confLookup = await _uow.Conferences.GetDictionaryAsync(token);
            string confAbbr = string.Empty;
            if (team.ConferenceId.HasValue && confLookup.TryGetValue(team.ConferenceId.Value, out var conf))
                confAbbr = conf.Abbreviation ?? string.Empty;

            var allRecords = await _uow.TeamRecords.GetByTeamAllYearsAsync(teamId, token);
            if (!allRecords.Any())
                throw new KeyNotFoundException($"No records found for team {teamId}.");

            var history       = allRecords.OrderByDescending(r => r.Year).ToList();
            var targetRecords = startYear.HasValue
                ? allRecords.Where(r => r.Year >= startYear.Value).ToList()
                : allRecords;

            var results = targetRecords.Select(r =>
            {
                var priorRecords = history.Where(h => h.Year < r.Year).Take(10).ToList();
                var avg = _rollingAverageService.Compute(r, priorRecords, useLiveSwap: false);
                return (object)new
                {
                    year = (int)r.Year, wins = (int)r.Wins, losses = (int)r.Losses,
                    seedRating = avg.SeedRating, trendRating = avg.TrendRating,
                    trendHistory = avg.TrendHistory, pedigreeRating = avg.PedigreeRating,
                    pedigreeHistory = avg.PedigreeHistory
                };
            }).ToList();

            return new TeamRollingAveragesResult(team.TeamId, team.TeamName, confAbbr, results);
        }

        // ── Named Rivalries ──────────────────────────────────────────────────────

        /// <summary>
        /// V2: team name lookup via Teams instead of Team.
        /// MatchupHistory table is shared — unchanged.
        /// </summary>
        public async Task<NamedRivalriesResult> GetNamedRivalriesV2Async(CancellationToken token = default)
        {
            var rivalries = await _uow.Lookups.GetMatchupHistoriesAsync(token);
            rivalries = rivalries
                .Where(m => m.RivalryName != null)
                .OrderBy(m => m.RivalryTier).ThenBy(m => m.RivalryName)
                .ToList();

            var Teams = await _uow.Teams.GetDictionaryByTeamIdAsync(token);

            var result = rivalries.Select(r =>
            {
                Teams.TryGetValue(r.Team1Id, out var t1);
                Teams.TryGetValue(r.Team2Id, out var t2);
                return (object)new
                {
                    r.Team1Id,
                    Team1Name      = t1?.TeamName ?? "Unknown",
                    Team1ShortName = t1?.Abbreviation ?? t1?.TeamName ?? "Unknown",
                    r.Team2Id,
                    Team2Name      = t2?.TeamName ?? "Unknown",
                    Team2ShortName = t2?.Abbreviation ?? t2?.TeamName ?? "Unknown",
                    r.RivalryName, r.RivalryTier, r.GamesPlayed,
                    r.AvgMargin, r.StDevMargin, r.UpsetRate, r.FirstPlayed, r.LastPlayed
                };
            }).ToList();

            return new NamedRivalriesResult(result);
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

            var allProjections = await _projectionCache.GetAllProjections(year, token);

            var games = teamGames.Select(g =>
            {
                bool isHome  = g.HomeId == teamId;
                var oppId    = isHome ? (g.AwayId ?? 0) : (g.HomeId ?? 0);
                Teams.TryGetValue(oppId, out var opp);
                var opponent  = opp?.TeamName ?? opp?.Abbreviation ?? "Unknown";
                var oppConf  = ConfAbbr(opp);

                bool isPlayed = (g.HomePoints ?? 0) > 0 || (g.AwayPoints ?? 0) > 0;

                int myPts  = isHome ? (g.HomePoints ?? 0) : (g.AwayPoints ?? 0);
                int oppPts = isHome ? (g.AwayPoints ?? 0) : (g.HomePoints ?? 0);
                bool won   = myPts > oppPts;

                double? projMy = null, projOpp = null;
                string confidence = "Unknown";
                if (allProjections.TryGetValue(g.GameId, out var pred))
                {
                    projMy   = isHome ? pred.PredictedTeamScore : pred.PredictedOpponentScore;
                    projOpp  = isHome ? pred.PredictedOpponentScore : pred.PredictedTeamScore;
                    confidence = pred.Confidence ?? "Unknown";
                }

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
                    SeasonType = g.SeasonType
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

            string GetConfAbbr(SaturdayPulse.Models.Teams? t)
            {
                if (t?.ConferenceId == null) return string.Empty;
                confLookup.TryGetValue(t.ConferenceId.Value, out var conf);
                return conf?.Abbreviation ?? string.Empty;
            }

            string GetConfName(SaturdayPulse.Models.Teams? t)
            {
                if (t?.ConferenceId == null) return string.Empty;
                confLookup.TryGetValue(t.ConferenceId.Value, out var conf);
                return conf?.Name ?? string.Empty;
            }

            var allProjections = await _projectionCache.GetAllProjections(targetYear, token);

            var results = games.Select(g =>
            {
                teams.TryGetValue(g.HomeId ?? 0, out var homeTeam);
                teams.TryGetValue(g.AwayId ?? 0, out var awayTeam);

                var homePoints = g.HomePoints ?? 0;
                var awayPoints = g.AwayPoints ?? 0;
                var isPlayed   = homePoints > 0 || awayPoints > 0;
                var actualOU   = homePoints + awayPoints;
                char location  = g.NeutralSite == true ? 'N' : 'H';

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
                    HomeConf      = GetConfAbbr(homeTeam),
                    HomeTier      = RatingCalculator.GetConferenceTier(GetConfName(homeTeam), g.HomeName),
                    HomePoints    = homePoints,
                    HomeProjScore = projHome,
                    AwayName      = g.AwayName,
                    AwayId        = g.AwayId,
                    AwayConf      = GetConfAbbr(awayTeam),
                    AwayTier      = RatingCalculator.GetConferenceTier(GetConfName(awayTeam), g.AwayName),
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

        // ── Private helpers ───────────────────────────────────────────────────────

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
    }
}
