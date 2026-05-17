using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.Models;
using SaturdayPulse.Utilities;

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

            var games = await _uow.GamesV2.GetByYearAsync(targetYear, token);
            games = games.OrderBy(g => g.Week).ToList();

            if (games.Count == 0) return new ScheduleResult(Array.Empty<object>());

            var confLookup = await _uow.Conferences.GetDictionaryAsync(token);
            var teamsV2    = await _uow.TeamsV2.GetDictionaryByTeamIdAsync(token);

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

            var results = games.Select(g =>
            {
                teamsV2.TryGetValue(g.HomeId ?? 0, out var homeTeam);
                teamsV2.TryGetValue(g.AwayId ?? 0, out var awayTeam);

                bool homeWon   = (g.HomePoints ?? 0) >= (g.AwayPoints ?? 0);
                var winnerId   = homeWon ? g.HomeId    : g.AwayId;
                var winnerName = homeWon ? g.HomeName  : g.AwayName;
                var winnerTeam = homeWon ? homeTeam    : awayTeam;
                var wPoints    = homeWon ? (g.HomePoints ?? 0) : (g.AwayPoints ?? 0);

                var loserId    = homeWon ? g.AwayId    : g.HomeId;
                var loserName  = homeWon ? g.AwayName  : g.HomeName;
                var loserTeam  = homeWon ? awayTeam    : homeTeam;
                var lPoints    = homeWon ? (g.AwayPoints ?? 0) : (g.HomePoints ?? 0);

                char location  = g.NeutralSite == true ? 'N' : homeWon ? 'W' : 'L';

                double? projWinner = null, projLoser = null;
                if (allProjections.TryGetValue(g.GameId, out var pred))
                {
                    projWinner = Math.Max(0, Math.Round(pred.PredictedTeamScore, 1));
                    projLoser  = Math.Max(0, Math.Round(pred.PredictedOpponentScore, 1));
                }

                var actualOU = wPoints + lPoints;
                var projOU   = projWinner.HasValue && projLoser.HasValue
                               ? (double?)Math.Round(projWinner.Value + projLoser.Value, 1) : null;

                return (object)new
                {
                    Id              = g.GameId,
                    g.Year,
                    g.Week,
                    GameDate        = g.GameDate,
                    GameDay         = g.GameDay,
                    WinnerName      = winnerName,
                    WinnerShortName = winnerTeam?.Abbreviation ?? winnerName,
                    WinnerId        = winnerId,
                    WinnerConf      = GetConfAbbr(winnerTeam),
                    WinnerTier      = RatingCalculator.GetConferenceTier(GetConfName(winnerTeam), winnerName),
                    WPoints         = wPoints,
                    LoserName       = loserName,
                    LoserShortName  = loserTeam?.Abbreviation ?? loserName,
                    LoserId         = loserId,
                    LoserConf       = GetConfAbbr(loserTeam),
                    LoserTier       = RatingCalculator.GetConferenceTier(GetConfName(loserTeam), loserName),
                    LPoints         = lPoints,
                    Location        = location,
                    ActualOU        = actualOU,
                    ProjWinnerScore = projWinner,
                    ProjLoserScore  = projLoser,
                    ProjOU          = projOU,
                    // Future rebind fields — ignored by client today
                    HomeId          = g.HomeId,
                    HomeName        = g.HomeName,
                    AwayId          = g.AwayId,
                    AwayName        = g.AwayName
                };
            }).ToList();

            return new ScheduleResult(results);
        }

        // ── Teams and Rivalries ──────────────────────────────────────────────────

        /// <summary>
        /// V2: reads rivalry game history from Games table (CFBD-sourced).
        /// Legacy equivalent: GetRivalryHistoryAsync() which reads from Game table.
        ///
        /// Team lookup via TeamsV2 + Conferences instead of Team.
        /// Winner/loser derived from home/away points; home team defaults for unplayed.
        /// Projection and rivalry metadata (MatchupHistory, AvgScoreDeltas) unchanged —
        /// those tables are shared and will be rebuilt via Developer backfill.
        /// </summary>
        public async Task<RivalryHistoryResult> GetRivalryHistoryV2Async(
            int team1Id, int team2Id, int years, CancellationToken token = default)
        {
            var teamsV2    = await _uow.TeamsV2.GetDictionaryByTeamIdAsync(token);
            var confLookup = await _uow.Conferences.GetDictionaryAsync(token);

            if (!teamsV2.TryGetValue(team1Id, out var team1))
                throw new KeyNotFoundException($"Team {team1Id} not found.");
            if (!teamsV2.TryGetValue(team2Id, out var team2))
                throw new KeyNotFoundException($"Team {team2Id} not found.");

            string ConfAbbr(Teams? t)
            {
                if (t?.ConferenceId == null) return string.Empty;
                confLookup.TryGetValue(t.ConferenceId.Value, out var conf);
                return conf?.Abbreviation ?? string.Empty;
            }

            var cutoffYear     = DateTime.Now.Year - years;
            var games          = await _uow.GamesV2.GetRivalryHistoryAsync(team1Id, team2Id, cutoffYear, token);
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
                    char location    = g.NeutralSite == true ? 'N' : team1IsHome ? 'W' : 'L';

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
                    ? Math.Max(-35.0, Math.Min(35.0, (double)asd.AverageScoreDelta)) : 7.0;
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
            var teams      = await _uow.TeamsV2.GetAllAsync(token);
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
            var teamsV2    = await _uow.TeamsV2.GetDictionaryByTeamIdAsync(token);
            var confLookup = await _uow.Conferences.GetDictionaryAsync(token);
            var confByYear = await _uow.TeamsConferenceHistory.GetConferenceIdsByYearAsync(targetYear, token);

            // Helper: get conference abbreviation for a team in the target year
            string ConfAbbrForYear(int teamId)
            {
                if (!confByYear.TryGetValue(teamId, out var confId)) return string.Empty;
                confLookup.TryGetValue(confId, out var conf);
                return conf?.Abbreviation ?? string.Empty;
            }

            var allGames = await _uow.GamesV2.GetByYearAsync(targetYear, token);

            if (allGames.Any())
            {
                var maxWeek = allGames.Max(g => g.Week);
                allGames = allGames.Where(g => g.Week < maxWeek).ToList();
            }

            var allProjections = await _projectionCache.GetAllProjections(targetYear, token);

            // Target teams: FBS, has a conference assignment this year, not IND/Pac-12,
            // optionally filtered by conference param
            var targetTeams = teamsV2.Values
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
                    teamsV2.TryGetValue(oppId, out var opp);
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
            var teamsV2    = await _uow.TeamsV2.GetDictionaryByTeamIdAsync(token);
            var confLookup = await _uow.Conferences.GetDictionaryAsync(token);
            var confByYear = await _uow.TeamsConferenceHistory.GetConferenceIdsByYearAsync(year, token);
            var records    = await _uow.TeamRecords.GetByYearAsync(year, token);
            var recordById = records.ToDictionary(tr => tr.TeamID);

            var fbsTeamsThisYear = teamsV2.Values
                .Where(t => string.Equals(t.Division, "fbs", StringComparison.OrdinalIgnoreCase)
                         && confByYear.ContainsKey(t.TeamId))
                .ToList();

            var allGames  = await _uow.GamesV2.GetByYearAsync(year, token);
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
                    TeamId            = t.TeamId,
                    TeamName          = t.TeamName,
                    Conference        = confAbbr,
                    Division          = RatingCalculator.GetDivision(t.TeamName, confAbbr),
                    ConferenceWins    = confWins,
                    ConferenceLosses  = confLosses,
                    OverallWins       = rec != null ? (int)rec.Wins   : 0,
                    OverallLosses     = rec != null ? (int)rec.Losses : 0,
                    ConfPointsFor     = ptsFor,
                    ConfPointsAgainst = ptsAgainst
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
            var teamsV2    = await _uow.TeamsV2.GetDictionaryByTeamIdAsync(token);
            var confLookup = await _uow.Conferences.GetDictionaryAsync(token);
            var confByYear = await _uow.TeamsConferenceHistory.GetConferenceIdsByYearAsync(year, token);
            var records    = await _uow.TeamRecords.GetByYearAsync(year, token);
            var recordById = records.ToDictionary(tr => tr.TeamID);

            var fbsTeamsThisYear = teamsV2.Values
                .Where(t => string.Equals(t.Division, "fbs", StringComparison.OrdinalIgnoreCase)
                         && confByYear.ContainsKey(t.TeamId))
                .ToList();

            var allGames = await _uow.GamesV2.GetByYearAsync(year, token);

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
                    ConferenceWins    = actualWins + projWins,
                    ConferenceLosses  = actualLosses + projLosses,
                    OverallWins       = rec != null ? (int)rec.Wins   : 0,
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
