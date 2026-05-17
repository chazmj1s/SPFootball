using SaturdayPulse.Contracts;
using SaturdayPulse.Contracts.Requests;
using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.Models;
using static Azure.Core.HttpHeader;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Encapsulates all data-access and business logic for the production read-only
    /// endpoints. Pass 2 complete: all EF queries moved to repositories.
    /// </summary>
    public partial class ProductionGameDataService
    {
        private readonly IUnitOfWork               _uow;
        private readonly GamePredictionService     _predictionService;
        private readonly ProjectionCacheService    _projectionCache;
        private readonly WeeklyRankingsService     _weeklyRankingsService;
        private readonly RollingAverageService     _rollingAverageService;
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

        // ── Diagnostics ──────────────────────────────────────────────────────────

        public async Task<DiagnosticInfo> GetDiagnosticAsync(CancellationToken token = default)
        {
            var allTeams    = await _uow.Team.GetAllAsync(token);
            var allGames    = await _uow.Game.GetByYearAsync(DateTime.Now.Year, token);
            var allRecords  = await _uow.TeamRecords.GetByYearAsync(DateTime.Now.Year, token);

            // For totals we need counts across all years — use lookup methods
            var totalTeams             = allTeams.Count;
            var yearRecords            = await _uow.TeamRecords.GetSinceYearWithTeamsAsync(1960, token);
            var totalGames             = (await _uow.Game.GetPlayedGamesSinceYearAsync(1960, token)).Count;
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
                TeamName          = tr.Team!.TeamName,
                Record            = $"{tr.Wins}-{tr.Losses}",
                tr.Wins, tr.Losses, tr.PointsFor, tr.PointsAgainst,
                PointDifferential = tr.PointsFor - tr.PointsAgainst,
                tr.BaseSOS, tr.SubSOS, tr.CombinedSOS, tr.PowerRating
            }).ToList();

            var filters = (object)new { wins, losses, minWins, maxWins, startYear, endYear, minPowerRating, maxPowerRating, limit };
            return new TeamRecordsQueryResult(mapped.Count, filters, mapped);
        }

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

            var historyByTeam = historicalRecords
                .GroupBy(tr => tr.TeamID)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Year).ToList());

            var results = currentRecords.Select(r =>
            {
                historyByTeam.TryGetValue(r.TeamID, out var history);
                history ??= [];
                var avg = _rollingAverageService.Compute(r, history, useLiveSwap: false);
                return (object)new
                {
                    teamId = r.TeamID, teamName = r.Team?.TeamName, conference = r.Team?.ConferenceAbbr,
                    seedRating = avg.SeedRating, trendRating = avg.TrendRating,
                    trendHistory = avg.TrendHistory, pedigreeRating = avg.PedigreeRating,
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
            var team = await _uow.Team.GetByIdAsync(teamId, token)
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
                var avg = _rollingAverageService.Compute(r, priorRecords, useLiveSwap: false);
                return (object)new
                {
                    year = (int)r.Year, wins = (int)r.Wins, losses = (int)r.Losses,
                    seedRating = avg.SeedRating, trendRating = avg.TrendRating,
                    trendHistory = avg.TrendHistory, pedigreeRating = avg.PedigreeRating,
                    pedigreeHistory = avg.PedigreeHistory
                };
            }).ToList();

            return new TeamRollingAveragesResult(team.TeamID, team.TeamName, team.ConferenceAbbr, results);
        }

        // ── Rankings ─────────────────────────────────────────────────────────────

        public async Task<RivalriesResult> GetRivalriesAsync(
            string? tier, int? minGames, double? minVarianceRatio, CancellationToken token = default)
        {
            var matchups = await _uow.Lookups.GetMatchupHistoriesAsync(token);

            if (!string.IsNullOrEmpty(tier) && !tier.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                matchups = matchups.Where(m => m.RivalryTier == tier).ToList();
            if (minGames.HasValue)
                matchups = matchups.Where(m => m.GamesPlayed >= minGames.Value).ToList();

            matchups = matchups.OrderByDescending(m => m.GamesPlayed).ToList();

            var teamDict = await _uow.Team.GetTeamDictionaryAsync(token);
            var asdList  = await _uow.Lookups.GetAvgScoreDeltasAsync(token);
            var avgStDev = asdList.Any() ? asdList.Average(a => (double)a.StDevP) : 15.0;

            var results = new List<object>();
            foreach (var m in matchups)
            {
                var team1         = teamDict.TryGetValue(m.Team1Id, out var t1) ? t1.TeamName : "Unknown";
                var team2         = teamDict.TryGetValue(m.Team2Id, out var t2) ? t2.TeamName : "Unknown";
                var varianceRatio = (double)m.StDevMargin / avgStDev;

                if (minVarianceRatio.HasValue && varianceRatio < minVarianceRatio.Value) continue;

                results.Add(new
                {
                    team1, team2, rivalryName = m.RivalryName ?? "N/A", tier = m.RivalryTier ?? "N/A",
                    gamesPlayed = m.GamesPlayed, avgMargin = Math.Round((double)m.AvgMargin, 1),
                    stDevMargin = Math.Round((double)m.StDevMargin, 1), upsetRate = Math.Round((double)m.UpsetRate, 3),
                    varianceRatio = Math.Round(varianceRatio, 2), seriesAge = m.LastPlayed - m.FirstPlayed,
                    firstPlayed = m.FirstPlayed, lastPlayed = m.LastPlayed
                });
            }

            return new RivalriesResult(results.Count, matchups.Count,
                new { tier = tier ?? "ALL", minGames = minGames ?? 0, minVarianceRatio = minVarianceRatio ?? 0.0 },
                results);
        }

        public async Task<PowerRankingsResult> GetPowerRankingsAsync(int? year, int? throughWeek, CancellationToken token = default)
        {
           var targetYear = year ?? DateTime.Now.Year;

            if (throughWeek.HasValue)
            {
                var weekly = await _uow.WeeklyRankings.GetByYearAndWeekAsync(targetYear, throughWeek.Value, token);

                if (!weekly.Any())
                    throw new KeyNotFoundException(
                        $"No weekly rankings found for year {targetYear} week {throughWeek}.");

                //Team data
                var teamDict = await _uow.Team.GetTeamDictionaryAsync(token);

                //Rolling Averages
                var currentRecords = await _uow.TeamRecords.GetByYearAsync(targetYear, token);
                var currentRecordLookup = currentRecords.ToDictionary(r => r.TeamID);
                var historicalRecords = await _uow.TeamRecords.GetHistoricalAsync(targetYear - 10, targetYear, token);
                var historyByTeam = historicalRecords
                    .GroupBy(tr => tr.TeamID)
                    .ToDictionary(
                        g => g.Key,
                        g => g.OrderBy(r => r.Year).ToList());

                var result = weekly
                    .Where(wr => wr.Ranking.HasValue)
                    .OrderBy(wr => wr.OverallRank)
                    .Select(wr =>
                    {
                        teamDict.TryGetValue(wr.TeamID, out var t);
                        currentRecordLookup.TryGetValue(wr.TeamID, out var currentRecord);
                        historyByTeam.TryGetValue(wr.TeamID, out var history);
                        history ??= [];

                        return new PowerRankingRowResponse
                        {
                            TeamID = wr.TeamID,
                            TeamName = t?.TeamName,
                            Conference = t?.Conference,
                            ConferenceAbbr = t?.ConferenceAbbr,
                            Division = t?.Division,

                            Tier = RatingCalculator.GetConferenceTier(t?.Conference, t?.TeamName),

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
                                 .Select(h => (double)(h.TrendRating ?? 0m))
                                 .ToList(),

                            PedigreeHistory = history
                                 .Select(h => (double)(h.PedigreeRating ?? 0m))
                                 .ToList()
                        };
                    })
                    .ToList();


                return new PowerRankingsResult(true, result);
            }
            else
            {
                var teamRecords = await _uow.TeamRecords.GetByYearWithTeamsAsync(targetYear, token);
                var ranked      = teamRecords.Where(tr => tr.Ranking.HasValue).ToList();

                var withTiers = ranked
                    .Select(tr => new { TeamRecord = tr, Tier = RatingCalculator.GetConferenceTier(tr.Team?.Conference, tr.Team?.TeamName) })
                    .OrderByDescending(t => t.TeamRecord.Ranking)
                    .ToList();

                var withOverallRank = withTiers
                    .Select((t, i) => new { t.TeamRecord, t.Tier, OverallRank = i + 1 })
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
                    .OrderByDescending(t => t.TeamRecord.Ranking)
                    .Select(t => new PowerRankingRowResponse
                    {
                        TeamID         = t.TeamRecord.TeamID,
                        TeamName       = t.TeamRecord.Team?.TeamName,
                        Conference     = t.TeamRecord.Team?.Conference,
                        ConferenceAbbr = t.TeamRecord.Team?.ConferenceAbbr,
                        Division       = t.TeamRecord.Team?.Division,
                        Tier           = t.Tier,
                        OverallRank    = t.OverallRank,
                        TierRank       = tierRankLookup[t.TeamRecord.TeamID],
                        Ranking        = (double?)t.TeamRecord.Ranking,
                        Year           = t.TeamRecord.Year,
                        Wins           = t.TeamRecord.Wins,
                        Losses         = t.TeamRecord.Losses,
                        BaseSOS        = (double?)t.TeamRecord.BaseSOS,
                        CombinedSOS    = (double?)t.TeamRecord.CombinedSOS
                    }).ToList();

                return new PowerRankingsResult(false, rankings);
            }
        }

        // ── Schedule ─────────────────────────────────────────────────────────────

        public async Task<ScheduleResult> GetScheduleAsync(int? year, CancellationToken token = default)
        {
            var targetYear = year ?? DateTime.Now.Year;

            var games = await _uow.Game.GetByYearAsync(targetYear, token);
            games = games.OrderBy(g => g.Week).ToList();

            if (games.Count == 0) return new ScheduleResult(Array.Empty<object>());

            var teams          = await _uow.Team.GetTeamDictionaryAsync(token);
            var allProjections = await _projectionCache.GetAllProjections(targetYear, token);

            var results = games.Select(g =>
            {
                teams.TryGetValue(g.WinnerId, out var winner);
                teams.TryGetValue(g.LoserId,  out var loser);

                double? projWinner = null, projLoser = null;
                if (allProjections.TryGetValue(g.Id, out var pred))
                {
                    projWinner = Math.Max(0, Math.Round(pred.PredictedTeamScore, 1));
                    projLoser  = Math.Max(0, Math.Round(pred.PredictedOpponentScore, 1));
                }

                var actualOU = g.WPoints + g.LPoints;
                var projOU   = projWinner.HasValue && projLoser.HasValue
                               ? (double?)Math.Round(projWinner.Value + projLoser.Value, 1) : null;

                return (object)new
                {
                    g.Id, g.Year, g.Week, GameDate = g.GameDate, GameDay = g.GameDay,
                    WinnerName = g.WinnerName, WinnerShortName = winner?.ShortName ?? g.WinnerName,
                    WinnerId = g.WinnerId, WinnerConf = winner?.ConferenceAbbr ?? winner?.Conference ?? "",
                    WinnerTier = RatingCalculator.GetConferenceTier(winner?.Conference, winner?.TeamName),
                    WPoints = g.WPoints,
                    LoserName = g.LoserName, LoserShortName = loser?.ShortName ?? g.LoserName,
                    LoserId = g.LoserId, LoserConf = loser?.ConferenceAbbr ?? loser?.Conference ?? "",
                    LoserTier = RatingCalculator.GetConferenceTier(loser?.Conference, loser?.TeamName),
                    LPoints = g.LPoints, Location = g.Location,
                    ActualOU = actualOU, ProjWinnerScore = projWinner,
                    ProjLoserScore = projLoser, ProjOU = projOU
                };
            }).ToList();

            return new ScheduleResult(results);
        }

        // ── Teams and Rivalries ──────────────────────────────────────────────────

        public async Task<TeamsResult> GetTeamsAsync(CancellationToken token = default)
        {
            var teams = await _uow.Team.GetAllAsync(token);
            var result = teams.Select(t => (object)new
            {
                t.TeamID, t.TeamName, ShortName = t.ShortName ?? t.TeamName,
                t.Conference, ConferenceAbbr = t.ConferenceAbbr ?? "", t.Division,
                Tier = RatingCalculator.GetConferenceTier(t.Conference, t.TeamName)
            }).ToList();

            return new TeamsResult(result);
        }

        public async Task<NamedRivalriesResult> GetNamedRivalriesAsync(CancellationToken token = default)
        {
            var rivalries = await _uow.Lookups.GetMatchupHistoriesAsync(token);
            rivalries = rivalries
                .Where(m => m.RivalryName != null)
                .OrderBy(m => m.RivalryTier).ThenBy(m => m.RivalryName)
                .ToList();

            var teams = await _uow.Team.GetTeamDictionaryAsync(token);

            var result = rivalries.Select(r =>
            {
                teams.TryGetValue(r.Team1Id, out var t1);
                teams.TryGetValue(r.Team2Id, out var t2);
                return (object)new
                {
                    r.Team1Id, Team1Name = t1?.TeamName ?? "Unknown", Team1ShortName = t1?.ShortName ?? t1?.TeamName ?? "Unknown",
                    r.Team2Id, Team2Name = t2?.TeamName ?? "Unknown", Team2ShortName = t2?.ShortName ?? t2?.TeamName ?? "Unknown",
                    r.RivalryName, r.RivalryTier, r.GamesPlayed,
                    r.AvgMargin, r.StDevMargin, r.UpsetRate, r.FirstPlayed, r.LastPlayed
                };
            }).ToList();

            return new NamedRivalriesResult(result);
        }

        public async Task<TeamHistoryResult> GetTeamHistoryAsync(
            int teamId, int years, CancellationToken token = default)
        {
            var team = await _uow.Team.GetByIdAsync(teamId, token)
                       ?? throw new KeyNotFoundException($"Team {teamId} not found.");

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
                Year = (int)r.Year, r.Wins, r.Losses, Record = $"{r.Wins}-{r.Losses}",
                PowerRating = r.Ranking, BaseSOS = r.BaseSOS, CombinedSOS = r.CombinedSOS,
                OverallRank = ranksByYear.GetValueOrDefault(r.Year, 0),
                Tier = RatingCalculator.GetConferenceTier(team.Conference, team.TeamName)
            }).ToList();

            return new TeamHistoryResult(teamId, team.TeamName, team.ShortName ?? team.TeamName, team.ConferenceAbbr, history);
        }

        public async Task<RivalryHistoryResult> GetRivalryHistoryAsync(
            int team1Id, int team2Id, int years, CancellationToken token = default)
        {
            var team1 = await _uow.Team.GetByIdAsync(team1Id, token)
                        ?? throw new KeyNotFoundException($"Team {team1Id} not found.");
            var team2 = await _uow.Team.GetByIdAsync(team2Id, token)
                        ?? throw new KeyNotFoundException($"Team {team2Id} not found.");

            var cutoffYear     = DateTime.Now.Year - years;
            var games          = await _uow.Game.GetRivalryHistoryAsync(team1Id, team2Id, cutoffYear, token);
            var rivalry        = await _uow.Lookups.GetMatchupHistoryAsync(team1Id, team2Id, token);
            var avgScoreDeltas = await _uow.Lookups.GetAvgScoreDeltasAsync(token);

            var avgTeamScore = games.Count > 0
                ? (games.Average(g => g.WPoints) + games.Average(g => g.LPoints)) / 2.0
                : 28.0;

            var history = games.Select(g =>
            {
                var team1Won   = g.WinnerId == team1Id;
                var team1Score = team1Won ? g.WPoints : g.LPoints;
                var team2Score = team1Won ? g.LPoints : g.WPoints;
                return (object)new
                {
                    g.Year, g.Week, g.Location,
                    Team1Score = team1Score, Team2Score = team2Score,
                    Margin = team1Score - team2Score, ActualOU = g.WPoints + g.LPoints,
                    Team1Won = team1Won, WinnerName = team1Won ? team1.TeamName : team2.TeamName,
                    Score = $"{g.WPoints}-{g.LPoints}"
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
                    Year = currentYear, ProjTeam1Score = projT1, ProjTeam2Score = projT2,
                    ProjMargin = Math.Round(projT1 - projT2, 1), ProjOU = Math.Round(projT1 + projT2, 1),
                    IsProjected = true
                };
            }

            return new RivalryHistoryResult(
                team1Id, team1.TeamName, team1.ShortName ?? team1.TeamName,
                team2Id, team2.TeamName, team2.ShortName ?? team2.TeamName,
                rivalry?.RivalryName, rivalry?.RivalryTier,
                rivalry?.GamesPlayed ?? history.Count,
                rivalry?.AvgMargin, rivalry?.UpsetRate,
                history, projection);
        }

        // ── Conference Standings ─────────────────────────────────────────────────

        public async Task<ChampionshipQualifiersResult> GetChampionshipQualifiersAsync(
            int? year, CancellationToken token = default)
        {
            var targetYear            = year ?? DateTime.Now.Year;
            var standingsByConference = await BuildConferenceStandingsAsync(targetYear, token);
            var service               = new ConferenceChampionshipService();
            var results               = BuildQualifierResponse(standingsByConference, service, includeContenders: false);
            return new ChampionshipQualifiersResult(results);
        }

        public async Task<ChampionshipQualifiersResult> GetProjectedChampionshipQualifiersAsync(
            int? year, int? throughWeek, CancellationToken token = default)
        {
            var targetYear            = year ?? DateTime.Now.Year;
            var standingsByConference = await BuildProjectedConferenceStandingsAsync(targetYear, throughWeek, token);
            var service               = new ConferenceChampionshipService();
            var results               = BuildQualifierResponse(standingsByConference, service, includeContenders: true, throughWeek: throughWeek);
            return new ChampionshipQualifiersResult(results);
        }

        public async Task<IReadOnlyList<object>> GetProjectedStandingsAsync(
            int? year, int? throughWeek, string? conference, CancellationToken token = default)
        {
            var targetYear = year ?? DateTime.Now.Year;
            var teams      = await _uow.Team.GetTeamDictionaryAsync(token);
            var fbsTeamIds = teams.Values.Where(t => t.Division == "FBS").Select(t => t.TeamID).ToHashSet();

            var allGames = await _uow.Game.GetByYearAsync(targetYear, token);
            var maxWeek = allGames.Max(g => g.Week);
            allGames = allGames.Where(g => g.Week < maxWeek).ToList();

            bool IsConfGame(Game g) =>
                fbsTeamIds.Contains(g.WinnerId) && fbsTeamIds.Contains(g.LoserId) &&
                teams.TryGetValue(g.WinnerId, out var w) && teams.TryGetValue(g.LoserId, out var l) &&
                !string.IsNullOrEmpty(w.ConferenceAbbr) && w.ConferenceAbbr == l.ConferenceAbbr;

            var allProjections = await _projectionCache.GetAllProjections(targetYear, token);

            var targetTeams = teams.Values
                .Where(t => t.Division == "FBS" &&
                            !string.IsNullOrEmpty(t.ConferenceAbbr) &&
                            t.ConferenceAbbr != "IND" && t.ConferenceAbbr != "Pac-12" &&
                            (conference == null ||
                             t.ConferenceAbbr.Equals(conference, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var teamResults = targetTeams.Select(team =>
            {
                var teamConfGames = allGames
                    .Where(g => IsConfGame(g) && (g.WinnerId == team.TeamID || g.LoserId == team.TeamID))
                    .OrderBy(g => g.Week).ToList();

                int actualWins = 0, actualLosses = 0, projWins = 0, projLosses = 0;

                var gameDetails = teamConfGames.Select(g =>
                {
                    var isHome  = g.WinnerId == team.TeamID ? g.Location == 'W' : g.Location == 'L';
                    var oppId   = g.WinnerId == team.TeamID ? g.LoserId : g.WinnerId;
                    teams.TryGetValue(oppId, out var opp);
                    var oppName = opp?.ShortName ?? opp?.TeamName ?? "Unknown";

                    bool isPlayed = (g.WPoints > 0 || g.LPoints > 0) &&
                                    (!throughWeek.HasValue || g.Week <= throughWeek.Value);

                    if (isPlayed)
                    {
                        bool won      = g.WinnerId == team.TeamID;
                        if (won) actualWins++; else actualLosses++;
                        var teamScore = won ? g.WPoints : g.LPoints;
                        var oppScore  = won ? g.LPoints : g.WPoints;
                        return (object)new
                        {
                            g.Week, Opponent = oppName, Location = isHome ? "vs" : "@",
                            Result = won ? "W" : "L", Score = $"{teamScore}-{oppScore}",
                            ProjScore = (string?)null, Confidence = (string?)null,
                            Type = "Actual", NeutralSite = g.Location == 'N'
                        };
                    }
                    else
                    {
                        double projTeamScore = 0, projOppScore = 0;
                        string confidence    = "Unknown";
                        bool projWin         = false;

                        if (allProjections.TryGetValue(g.Id, out var pred))
                        {
                            bool teamIsWinner = g.WinnerId == team.TeamID;
                            projTeamScore = teamIsWinner ? pred.PredictedTeamScore : pred.PredictedOpponentScore;
                            projOppScore  = teamIsWinner ? pred.PredictedOpponentScore : pred.PredictedTeamScore;
                            confidence    = pred.Confidence ?? "Unknown";
                            projWin       = projTeamScore >= projOppScore;
                        }
                        else { projWin = isHome; }

                        if (projWin) projWins++; else projLosses++;

                        return (object)new
                        {
                            g.Week, Opponent = oppName, Location = isHome ? "vs" : "@",
                            Result = projWin ? "W" : "L", Score = (string?)null,
                            ProjScore = projTeamScore > 0 ? $"{Math.Round(projTeamScore)}-{Math.Round(projOppScore)}" : null,
                            Confidence = confidence, Type = "Projected", NeutralSite = g.Location == 'N'
                        };
                    }
                }).ToList();

                int totalWins   = actualWins   + projWins;
                int totalLosses = actualLosses + projLosses;
                int total       = totalWins + totalLosses;

                return (object)new
                {
                    team.TeamName, Conference = team.ConferenceAbbr,
                    Division = RatingCalculator.GetDivision(team.TeamName, team.ConferenceAbbr),
                    ActualWins = actualWins, ActualLosses = actualLosses,
                    ProjectedWins = totalWins, ProjectedLosses = totalLosses,
                    ProjectedWinPct = Math.Round(total > 0 ? (double)totalWins / total : 0.0, 3),
                    Games = gameDetails,
                    SimulatedThrough = throughWeek.HasValue ? $"Week {throughWeek}" : "Current"
                };
            }).ToList();

            return teamResults
                .OrderBy(t => RatingCalculator.ConferenceDisplayOrder(((dynamic)t).Conference))
                .ThenByDescending(t => ((dynamic)t).ProjectedWinPct)
                .ThenByDescending(t => ((dynamic)t).ProjectedWins)
                .ToList();
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private IReadOnlyList<object> BuildQualifierResponse(
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
                    var q1 = new { r.Qualifier1.TeamName, r.Qualifier1.ConferenceWins, r.Qualifier1.ConferenceLosses, r.Qualifier1.OverallWins, r.Qualifier1.OverallLosses, r.Qualifier1.Division };
                    var q2 = new { r.Qualifier2.TeamName, r.Qualifier2.ConferenceWins, r.Qualifier2.ConferenceLosses, r.Qualifier2.OverallWins, r.Qualifier2.OverallLosses, r.Qualifier2.Division };

                    if (includeContenders)
                        return (object)new
                        {
                            r.Conference, r.Format, Qualifier1 = q1, Qualifier2 = q2,
                            Contenders = r.Contenders.Select(c => new { c.TeamName, c.ConferenceWins, c.ConferenceLosses, c.ConferenceRecord }).ToList(),
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

        private async Task<Dictionary<string, List<ConferenceStanding>>> BuildConferenceStandingsAsync(
            int year, CancellationToken token)
        {
            var teams      = await _uow.Team.GetTeamDictionaryAsync(token);
            var records    = await _uow.TeamRecords.GetByYearAsync(year, token);
            var recordById = records.ToDictionary(tr => tr.TeamID);
            var fbsTeamIds = teams.Values.Where(t => t.Division == "FBS").Select(t => t.TeamID).ToHashSet();

            var allGames  = await _uow.Game.GetByYearUpToWeekAsync(year, 15, token);
            var confGames = allGames.Where(g =>
                fbsTeamIds.Contains(g.WinnerId) && fbsTeamIds.Contains(g.LoserId) &&
                teams.TryGetValue(g.WinnerId, out var w) && teams.TryGetValue(g.LoserId, out var l) &&
                w.ConferenceAbbr == l.ConferenceAbbr && !string.IsNullOrEmpty(w.ConferenceAbbr)
            ).ToList();

            var confRecords = teams.Values
                .Where(t => t.Division == "FBS" && !string.IsNullOrEmpty(t.ConferenceAbbr))
                .Select(t =>
                {
                    recordById.TryGetValue(t.TeamID, out var rec);
                    return new ConferenceStanding
                    {
                        TeamId = t.TeamID, TeamName = t.TeamName, Conference = t.ConferenceAbbr,
                        Division          = RatingCalculator.GetDivision(t.TeamName, t.ConferenceAbbr),
                        ConferenceWins    = confGames.Count(g => g.WinnerId == t.TeamID),
                        ConferenceLosses  = confGames.Count(g => g.LoserId  == t.TeamID),
                        OverallWins       = rec != null ? (int)rec.Wins   : 0,
                        OverallLosses     = rec != null ? (int)rec.Losses : 0,
                        ConfPointsFor     = confGames.Where(g => g.WinnerId == t.TeamID).Sum(g => g.WPoints) +
                                            confGames.Where(g => g.LoserId  == t.TeamID).Sum(g => g.LPoints),
                        ConfPointsAgainst = confGames.Where(g => g.WinnerId == t.TeamID).Sum(g => g.LPoints) +
                                            confGames.Where(g => g.LoserId  == t.TeamID).Sum(g => g.WPoints)
                    };
                }).ToList();

            EnrichHeadToHead(confRecords, confGames);
            EnrichSOS(confRecords);

            return confRecords
                .Where(s => s.Conference != "IND" && s.Conference != "Pac-12" && !string.IsNullOrEmpty(s.Conference))
                .GroupBy(s => s.Conference)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        private async Task<Dictionary<string, List<ConferenceStanding>>> BuildProjectedConferenceStandingsAsync(
            int year, int? throughWeek, CancellationToken token)
        {
            var teams      = await _uow.Team.GetTeamDictionaryAsync(token);
            var records    = await _uow.TeamRecords.GetByYearAsync(year, token);
            var recordById = records.ToDictionary(tr => tr.TeamID);
            var fbsTeamIds = teams.Values.Where(t => t.Division == "FBS").Select(t => t.TeamID).ToHashSet();

            var allGames = await _uow.Game.GetByYearAsync(year, token);
            var maxWeek = allGames.Max(g => g.Week);
            allGames = allGames.Where(g => g.Week < maxWeek).ToList();

            bool IsConfGame(Game g) =>
                fbsTeamIds.Contains(g.WinnerId) && fbsTeamIds.Contains(g.LoserId) &&
                teams.TryGetValue(g.WinnerId, out var w) && teams.TryGetValue(g.LoserId, out var l) &&
                !string.IsNullOrEmpty(w.ConferenceAbbr) && w.ConferenceAbbr == l.ConferenceAbbr;

            var playedConfGames = allGames
                .Where(g => IsConfGame(g) && (g.WPoints > 0 || g.LPoints > 0) &&
                            (!throughWeek.HasValue || g.Week <= throughWeek.Value)).ToList();

            var unplayedConfGames = allGames
                .Where(g => IsConfGame(g) &&
                            (g.WPoints == 0 && g.LPoints == 0 ||
                             throughWeek.HasValue && g.Week > throughWeek.Value)).ToList();

            var allProjections = await _projectionCache.GetAllProjections(year, token);

            var projectedResults = unplayedConfGames.Select(g =>
            {
                if (allProjections.TryGetValue(g.Id, out var pred))
                {
                    bool winnerWins = pred.PredictedTeamScore >= pred.PredictedOpponentScore;
                    return winnerWins
                        ? (WinnerId: g.WinnerId, LoserId: g.LoserId, GameId: g.Id)
                        : (WinnerId: g.LoserId,  LoserId: g.WinnerId, GameId: g.Id);
                }
                return (WinnerId: g.WinnerId, LoserId: g.LoserId, GameId: g.Id);
            }).ToList();

            var confRecords = teams.Values
                .Where(t => t.Division == "FBS" && !string.IsNullOrEmpty(t.ConferenceAbbr))
                .Select(t =>
                {
                    var actualWins   = playedConfGames.Count(g => g.WinnerId == t.TeamID);
                    var actualLosses = playedConfGames.Count(g => g.LoserId  == t.TeamID);
                    var projWins     = projectedResults.Count(r => r.WinnerId == t.TeamID && unplayedConfGames.Any(g => g.Id == r.GameId));
                    var projLosses   = projectedResults.Count(r => r.LoserId  == t.TeamID && unplayedConfGames.Any(g => g.Id == r.GameId));
                    recordById.TryGetValue(t.TeamID, out var rec);

                    return new ConferenceStanding
                    {
                        TeamId = t.TeamID, TeamName = t.TeamName, Conference = t.ConferenceAbbr,
                        Division          = RatingCalculator.GetDivision(t.TeamName, t.ConferenceAbbr),
                        ConferenceWins    = actualWins + projWins,
                        ConferenceLosses  = actualLosses + projLosses,
                        OverallWins       = rec != null ? (int)rec.Wins   : 0,
                        OverallLosses     = rec != null ? (int)rec.Losses : 0,
                        ConfPointsFor     = playedConfGames.Where(g => g.WinnerId == t.TeamID).Sum(g => g.WPoints) +
                                            playedConfGames.Where(g => g.LoserId  == t.TeamID).Sum(g => g.LPoints),
                        ConfPointsAgainst = playedConfGames.Where(g => g.WinnerId == t.TeamID).Sum(g => g.LPoints) +
                                            playedConfGames.Where(g => g.LoserId  == t.TeamID).Sum(g => g.WPoints)
                    };
                }).ToList();

            foreach (var standing in confRecords)
            {
                var playedH2H = playedConfGames
                    .Where(g => g.WinnerId == standing.TeamId || g.LoserId == standing.TeamId)
                    .GroupBy(g => g.WinnerId == standing.TeamId ? g.LoserId : g.WinnerId)
                    .ToDictionary(g => g.Key,
                        g => g.Count(game => game.WinnerId == standing.TeamId) >
                             g.Count(game => game.LoserId  == standing.TeamId));

                var projH2H = projectedResults
                    .Where(r => (r.WinnerId == standing.TeamId || r.LoserId == standing.TeamId) &&
                                !playedH2H.ContainsKey(r.WinnerId == standing.TeamId ? r.LoserId : r.WinnerId))
                    .GroupBy(r => r.WinnerId == standing.TeamId ? r.LoserId : r.WinnerId)
                    .ToDictionary(g => g.Key,
                        g => g.Count(r => r.WinnerId == standing.TeamId) >
                             g.Count(r => r.LoserId  == standing.TeamId));

                standing.HeadToHeadResults = playedH2H.Concat(projH2H)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }

            EnrichSOS(confRecords);

            return confRecords
                .Where(s => s.Conference != "IND" && s.Conference != "Pac-12" && !string.IsNullOrEmpty(s.Conference))
                .GroupBy(s => s.Conference)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        private static void EnrichHeadToHead(List<ConferenceStanding> standings, List<Game> confGames)
        {
            foreach (var standing in standings)
            {
                standing.HeadToHeadResults = confGames
                    .Where(g => g.WinnerId == standing.TeamId || g.LoserId == standing.TeamId)
                    .GroupBy(g => g.WinnerId == standing.TeamId ? g.LoserId : g.WinnerId)
                    .ToDictionary(g => g.Key,
                        g => g.Count(game => game.WinnerId == standing.TeamId) >
                             g.Count(game => game.LoserId  == standing.TeamId));
            }
        }

        private static void EnrichSOS(List<ConferenceStanding> standings)
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
