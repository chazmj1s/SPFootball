using SaturdayPulse.Contracts;
using SaturdayPulse.Contracts.Requests;
using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Encapsulates all data-access and business logic for the production read-only
    /// endpoints. All legacy methods removed 2026-05-19 — use V2 equivalents in
    /// ProductionGameDataService_V2.cs.
    /// </summary>
    public partial class ProductionGameDataService
    {
        private readonly IUnitOfWork                        _uow;
        private readonly GamePredictionService              _predictionService;
        private readonly ProjectionCacheService             _projectionCache;
        private readonly WeeklyRankingsService              _weeklyRankingsService;
        private readonly RollingAverageService              _rollingAverageService;
        private readonly ILogger<ProductionGameDataService> _logger;

        public ProductionGameDataService(
            IUnitOfWork uow,
            GamePredictionService predictionService,
            ProjectionCacheService projectionCache,
            WeeklyRankingsService weeklyRankingsService,
            RollingAverageService rollingAverageService,
            ILogger<ProductionGameDataService> logger)
        {
            _uow                   = uow;
            _predictionService     = predictionService;
            _projectionCache       = projectionCache;
            _weeklyRankingsService = weeklyRankingsService;
            _rollingAverageService = rollingAverageService;
            _logger                = logger;
        }

        // ── Predictions ──────────────────────────────────────────────────────────

        public Task<GamePrediction> PredictMatchupAsync(
            int year, string teamName, string opponentName, char location, int week,
            CancellationToken token = default)
            => _predictionService.PredictMatchup(year, teamName, opponentName, location, week, token);

        public Task<List<GamePrediction>> PredictMatchupsAsync(
            int year, List<MatchupRequest> matchups, CancellationToken token = default)
            => _predictionService.PredictMatchups(year, matchups, token);

        public Task<GamePrediction> PredictSandboxMatchupAsync(
            string teamName, int teamYear,
            string opponentName, int opponentYear,
            CancellationToken token = default)
            => _predictionService.PredictSandboxMatchupAsync(
                teamName, teamYear, opponentName, opponentYear, token);

        public async Task<List<int>> GetTeamAvailableYearsAsync(
            int teamId, CancellationToken token = default)
        {
            var years = await _uow.WeeklyRankings.GetDistinctYearWeeksAsync(token);
            return years
                .Where(yw => yw.Year >= 1965)
                .Select(yw => yw.Year)
                .Distinct()
                .OrderByDescending(y => y)
                .ToList();
        }

        // ── Diagnostics ──────────────────────────────────────────────────────────

        public async Task<DiagnosticInfo> GetDiagnosticAsync(CancellationToken token = default)
        {
            var allTeams    = await _uow.Teams.GetAllAsync(token);
            var yearRecords = await _uow.TeamRecords.GetSinceYearWithTeamsAsync(1960, token);
            var totalGames  = (await _uow.Games.GetPlayedGamesSinceYearAsync(1960, token)).Count;

            var totalTeams             = allTeams.Count;
            var totalRecords           = yearRecords.Count;
            var recordsWithPowerRating = yearRecords.Count(tr => tr.PowerRating.HasValue);

            var years = yearRecords
                .Where(tr => tr.PowerRating.HasValue)
                .Select(tr => tr.Year)
                .Distinct()
                .OrderBy(y => y)
                .ToList();

            var yearStats = years.Select(y => (object)new
            {
                year              = y,
                teamsWithRankings = yearRecords.Count(tr => tr.Year == y && tr.PowerRating.HasValue)
            }).ToList();

            return new DiagnosticInfo("Connected", totalTeams, totalGames, totalRecords,
                recordsWithPowerRating, years.Select(y => (object)y).ToList(), yearStats);
        }

        // ── Queries ───────────────────────────────────────────────────────────────

        public async Task<TeamRecordsQueryResult> QueryTeamRecordsAsync(
            int? wins, int? losses, int? minWins, int? maxWins,
            int? startYear, int? endYear,
            decimal? minPowerRating, decimal? maxPowerRating,
            int limit, CancellationToken token = default)
        {
            var results = await _uow.TeamRecords.QueryAsync(
                wins, losses, minWins, maxWins, startYear, endYear,
                minPowerRating, maxPowerRating, limit, token);

            var mapped = results.Select(tr => (object)new
            {
                tr.Year,
                TeamName          = tr.Teams!.TeamName,
                Record            = $"{tr.Wins}-{tr.Losses}",
                tr.Wins, tr.Losses, tr.PointsFor, tr.PointsAgainst,
                PointDifferential = tr.PointsFor - tr.PointsAgainst,
                tr.BaseSOS, tr.SubSOS, tr.CombinedSOS, tr.PowerRating
            }).ToList();

            var filters = (object)new { wins, losses, minWins, maxWins, startYear, endYear, minPowerRating, maxPowerRating, limit };
            return new TeamRecordsQueryResult(mapped.Count, filters, mapped);
        }

        // ── Rolling Averages ─────────────────────────────────────────────────────

        public async Task<RollingAveragesResult> GetRollingAveragesAsync(int? year, CancellationToken token = default)
        {
            var targetYear = year ?? DateTime.Now.Year;

            var currentRecords = await _uow.TeamRecords.GetFbsByYearAsync(targetYear, token);
            currentRecords = currentRecords
                .Where(r => r.TrendRating != null || r.PedigreeRating != null)
                .ToList();

            if (!currentRecords.Any())
                throw new KeyNotFoundException($"No rolling average data found for {targetYear}.");

            var historicalRecords = await _uow.TeamRecords.GetHistoricalAsync(targetYear - 10, targetYear, token);
            var historyByTeam     = historicalRecords
                .GroupBy(tr => tr.TeamID)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Year).ToList());

            var results = currentRecords.Select(r =>
            {
                historyByTeam.TryGetValue(r.TeamID, out var history);
                history ??= [];
                var avg = _rollingAverageService.Compute(r, history, useLiveSwap: false);
                return (object)new
                {
                    teamId          = r.TeamID,
                    teamName        = r.Teams?.TeamName,
                    conference      = r.Teams?.Conference?.Abbreviation,
                    seedRating      = avg.SeedRating,
                    trendRating     = avg.TrendRating,
                    trendHistory    = avg.TrendHistory,
                    pedigreeRating  = avg.PedigreeRating,
                    pedigreeHistory = avg.PedigreeHistory
                };
            })
            .OrderByDescending(r => ((dynamic)r).trendRating)
            .ToList();

            return new RollingAveragesResult(targetYear, results.Count, results);
        }

        public async Task<TeamRollingAveragesResult> GetTeamRollingAveragesAsync(
            int teamId, int? startYear, CancellationToken token = default)
        {
            var team = await _uow.Teams.GetByTeamIdAsync(teamId, token)
                       ?? throw new KeyNotFoundException($"Team {teamId} not found.");

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
                var avg          = _rollingAverageService.Compute(r, priorRecords, useLiveSwap: false);
                return (object)new
                {
                    year            = (int)r.Year,
                    wins            = (int)r.Wins,
                    losses          = (int)r.Losses,
                    seedRating      = avg.SeedRating,
                    trendRating     = avg.TrendRating,
                    trendHistory    = avg.TrendHistory,
                    pedigreeRating  = avg.PedigreeRating,
                    pedigreeHistory = avg.PedigreeHistory
                };
            }).ToList();

            return new TeamRollingAveragesResult(team.TeamId, team.TeamName, team.Conference?.Abbreviation, results);
        }

        // ── Rivalries ────────────────────────────────────────────────────────────

        public async Task<RivalriesResult> GetRivalriesAsync(
            string? tier, int? minGames, double? minVarianceRatio, CancellationToken token = default)
        {
            var matchups = await _uow.Lookups.GetMatchupHistoriesAsync(token);

            if (!string.IsNullOrEmpty(tier) && !tier.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                matchups = matchups.Where(m => m.RivalryTier == tier).ToList();
            if (minGames.HasValue)
                matchups = matchups.Where(m => m.GamesPlayed >= minGames.Value).ToList();

            matchups = matchups.OrderByDescending(m => m.GamesPlayed).ToList();

            var teamsById = await _uow.Teams.GetDictionaryByTeamIdAsync(token);
            var asdList   = await _uow.Lookups.GetAvgScoreDeltasAsync(token);
            var avgStDev  = asdList.Any() ? asdList.Average(a => (double)a.StDevP) : 15.0;

            var results = new List<object>();
            foreach (var m in matchups)
            {
                var team1         = teamsById.TryGetValue(m.Team1Id, out var t1) ? t1.TeamName : "Unknown";
                var team2         = teamsById.TryGetValue(m.Team2Id, out var t2) ? t2.TeamName : "Unknown";
                var varianceRatio = (double)m.StDevMargin / avgStDev;

                if (minVarianceRatio.HasValue && varianceRatio < minVarianceRatio.Value) continue;

                results.Add(new
                {
                    team1, team2,
                    rivalryName   = m.RivalryName ?? "N/A",
                    tier          = m.RivalryTier ?? "N/A",
                    gamesPlayed   = m.GamesPlayed,
                    avgMargin     = Math.Round((double)m.AvgMargin,   1),
                    stDevMargin   = Math.Round((double)m.StDevMargin, 1),
                    upsetRate     = Math.Round((double)m.UpsetRate,   3),
                    varianceRatio = Math.Round(varianceRatio,         2),
                    seriesAge     = m.LastPlayed - m.FirstPlayed,
                    firstPlayed   = m.FirstPlayed,
                    lastPlayed    = m.LastPlayed
                });
            }

            return new RivalriesResult(results.Count, matchups.Count,
                new { tier = tier ?? "ALL", minGames = minGames ?? 0, minVarianceRatio = minVarianceRatio ?? 0.0 },
                results);
        }

        // ── Team History ─────────────────────────────────────────────────────────

        public async Task<TeamHistoryResult> GetTeamHistoryAsync(
            int teamId, int years, CancellationToken token = default)
        {
            var team = await _uow.Teams.GetByTeamIdAsync(teamId, token)
                       ?? throw new KeyNotFoundException($"Team {teamId} not found.");

            var confLookup = await _uow.Conferences.GetDictionaryAsync(token);
            confLookup.TryGetValue(team.ConferenceId ?? 0, out var conf);
            var confName = conf?.Name         ?? string.Empty;
            var confAbbr = conf?.Abbreviation ?? string.Empty;

            var cutoffYear = (short)(DateTime.Now.Year - years);
            var records    = await _uow.TeamRecords.GetByTeamAllYearsAsync(teamId, token);
            records = records.Where(r => r.Year >= cutoffYear).ToList();

            var allYears    = records.Select(r => r.Year).Distinct().ToList();
            var ranksByYear = new Dictionary<short, int>();

            foreach (var yr in allYears)
            {
                var allRanked = await _uow.TeamRecords.GetRankedByYearAsync(yr, token);
                var idx       = allRanked.FindIndex(tr => tr.TeamID == teamId);
                if (idx >= 0) ranksByYear[yr] = idx + 1;
            }

            var history = records.Select(r => (object)new
            {
                Year        = (int)r.Year,
                r.Wins,
                r.Losses,
                Record      = $"{r.Wins}-{r.Losses}",
                PowerRating = r.Ranking,
                BaseSOS     = r.BaseSOS,
                CombinedSOS = r.CombinedSOS,
                OverallRank = ranksByYear.GetValueOrDefault(r.Year, 0),
                Tier        = RatingCalculator.GetConferenceTier(confName, team.TeamName)
            }).ToList();

            return new TeamHistoryResult(teamId, team.TeamName, team.Abbreviation ?? team.TeamName, confAbbr, history);
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
            Func<Teams?, string> getConfName)
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
                HomeTier = RatingCalculator.GetConferenceTier(getConfName(homeTeam), g.HomeName),
                HomePoints = homePoints,
                HomeProjScore = projHome,
                AwayName = g.AwayName,
                AwayId = g.AwayId,
                AwayConf = getConfAbbr(awayTeam),
                AwayTier = RatingCalculator.GetConferenceTier(getConfName(awayTeam), g.AwayName),
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
            };
        }


        internal static void EnrichSOS(List<ConferenceStanding> standings)
        {
            foreach (var standing in standings)
            {
                var wins  = standing.HeadToHeadResults.Values.Count(v => v);
                var total = standing.HeadToHeadResults.Count;
                standing.CommonOpponentWinPct = total > 0 ? (double)wins / total : 0.0;
            }

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
    }
}
