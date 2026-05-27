using Microsoft.Extensions.Options;
using SaturdayPulse.Configuration;
using SaturdayPulse.Contracts;
using SaturdayPulse.Contracts.Requests;
using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Computes and persists per-week power ranking snapshots into WeeklyRankings.
    ///
    /// Single source of truth pipeline:
    ///   Steps 1-13 : compute all metrics in memory
    ///   Step 14    : write WeeklyRankings snapshot (authoritative)
    ///   Step 15    : UpsertFromWeeklyRankingsAsync → TeamRecords synced from WR
    ///   Step 16    : RollingAverageService → Seed/Trend/Pedigree (skippable for backfill)
    ///   Step 17    : write Projections for remaining unplayed games
    ///
    /// Z-score pipeline now uses AvgScoreDifferential (replaces AvgScoreDelta):
    ///   - Expected margin derived from ExpandStrength(prior week Ranking) differential
    ///   - StdDev from AvgScoreDifferential.StdDevMargin
    ///   - Consistent with the prediction engine and the table's own construction
    ///
    /// For years with unplayed games (e.g. a future season), projected scores are
    /// substituted for missing actuals so the full pipeline can run meaningfully.
    /// Synthetic scores are never written to the Games table.
    /// WeeklyRankings and TeamRecords are overwritten by actuals as the season progresses.
    ///
    /// Season bootstrap: call DeveloperService.InitializeSeasonAsync before running
    /// the first week of a new season. This creates a week 0 snapshot from the prior
    /// year's final ratings, seeding TeamRecords and Projections correctly.
    /// </summary>
    public class WeeklyRankingsService
    {
        private readonly IUnitOfWork            _uow;
        private readonly MetricsConfiguration   _config;
        private readonly GamePredictionService  _predictionService;
        private readonly RollingAverageService  _rollingAverageService;

        public WeeklyRankingsService(
            IUnitOfWork uow,
            GamePredictionService predictionService,
            RollingAverageService rollingAverageService,
            IOptions<MetricsConfiguration> config)
        {
            _uow                   = uow;
            _config                = config.Value;
            _predictionService     = predictionService;
            _rollingAverageService = rollingAverageService;
        }

        /// <summary>
        /// Runs the full SOS → PowerRating → Ranking → Offense/Defense pipeline
        /// for all FBS teams through the specified week, then upserts into WeeklyRankings.
        ///
        /// computeRollingAverages: set false during bulk backfill — BackfillWeeklyRankingsAsync
        /// runs RollingAverageService once per year instead of once per week for performance.
        /// Default is true so production weekly calls always compute rolling averages.
        /// </summary>
        public async Task ComputeAndSaveAsync(
            int year, int week,
            CancellationToken token = default,
            bool computeRollingAverages = true)
        {
            // ── 1. Load reference data ────────────────────────────────────────────
            var allTeams             = await _uow.Teams.GetAllAsync(token);
            var fbsTeams             = allTeams.Where(t =>
                string.Equals(t.Division, "fbs", StringComparison.OrdinalIgnoreCase)).ToList();
            var fbsIds               = fbsTeams.Select(t => t.TeamId).ToHashSet();
            var avgScoreDifferentials = await _uow.Lookups.GetAvgScoreDifferentialsAsync(token);
            var matchupHistories     = await _uow.Lookups.GetMatchupHistoriesAsync(token);

            // Load prior week's WeeklyRankings for pregame strength — same source
            // the AvgScoreDifferential table was built from.
            var priorWeek     = Math.Max(week - 1, 0);
            var priorRankings = await _uow.WeeklyRankings
                .GetByYearAndWeekAsync(year, priorWeek, token);
            var priorByTeamId = priorRankings.ToDictionary(wr => wr.TeamID);

            // ── 2. Played games through this week ─────────────────────────────────
            var games = await _uow.Games.GetPlayedGamesByYearAndWeekAsync(year, week, token);

            // ── 2a. Load all games for the year — used in steps 2b and 17 ─────────
            var allYearGames = await _uow.Games.GetByYearAsync(year, token);
            var teamsDict    = await _uow.Teams.GetDictionaryByTeamIdAsync(token);

            // ── 2b. Substitute projected scores for unplayed games ────────────────
            var unplayedThisWeek = allYearGames
                .Where(g => g.Week <= week &&
                            (g.HomePoints ?? 0) == 0 &&
                            (g.AwayPoints ?? 0) == 0 &&
                            g.HomeId.HasValue && g.AwayId.HasValue &&
                            teamsDict.ContainsKey(g.HomeId.Value) &&
                            teamsDict.ContainsKey(g.AwayId.Value))
                .ToList();

            if (unplayedThisWeek.Count > 0)
            {
                var currentYearRecords = await _uow.TeamRecords.GetByYearAsync(year, token);
                var predictionYear     = currentYearRecords.Any() ? year : year - 1;

                var matchupRequests = unplayedThisWeek
                    .Select(g => new MatchupRequest
                    {
                        TeamName     = teamsDict[g.HomeId!.Value].TeamName,
                        OpponentName = teamsDict[g.AwayId!.Value].TeamName,
                        Location     = g.NeutralSite == true ? 'N' : 'W',
                        Week         = g.Week
                    })
                    .ToList();

                var predictions = await _predictionService.PredictMatchups(
                    predictionYear, matchupRequests, token);

                foreach (var g in unplayedThisWeek)
                {
                    var homeName = teamsDict[g.HomeId!.Value].TeamName;
                    var awayName = teamsDict[g.AwayId!.Value].TeamName;

                    var pred = predictions.FirstOrDefault(p =>
                        p.TeamName     == homeName &&
                        p.OpponentName == awayName &&
                        p.Week         == g.Week);

                    if (pred == null) continue;

                    var homePoints = (int)Math.Round(pred.PredictedTeamScore);
                    var awayPoints = (int)Math.Round(pred.PredictedOpponentScore);

                    if (homePoints == 0 && awayPoints == 0) homePoints = 14;

                    games.Add(new Games
                    {
                        GameId      = g.GameId,
                        HomeId      = g.HomeId,
                        AwayId      = g.AwayId,
                        HomeName    = g.HomeName,
                        AwayName    = g.AwayName,
                        HomePoints  = homePoints,
                        AwayPoints  = awayPoints,
                        NeutralSite = g.NeutralSite,
                        Week        = g.Week,
                        Year        = g.Year
                    });
                }
            }

            // ── 3. Raw stats per team [wins, losses, pf, pa] ──────────────────────
            var rawStats = fbsTeams.ToDictionary(t => t.TeamId, _ => new int[4]);

            foreach (var g in games)
            {
                var homeId   = g.HomeId ?? 0;
                var awayId   = g.AwayId ?? 0;
                var homePts  = g.HomePoints ?? 0;
                var awayPts  = g.AwayPoints ?? 0;
                bool homeWon = homePts >= awayPts;

                if (rawStats.TryGetValue(homeId, out var hs))
                {
                    if (homeWon) hs[0]++; else hs[1]++;
                    hs[2] += homePts; hs[3] += awayPts;
                }
                if (rawStats.TryGetValue(awayId, out var as_))
                {
                    if (!homeWon) as_[0]++; else as_[1]++;
                    as_[2] += awayPts; as_[3] += homePts;
                }
            }

            var winsLookup   = rawStats.ToDictionary(kvp => kvp.Key, kvp => kvp.Value[0]);
            var lossesLookup = rawStats.ToDictionary(kvp => kvp.Key, kvp => kvp.Value[1]);

            // ── 4. Game-participant rows (home + away perspective) ─────────────────
            var teamById = allTeams.ToDictionary(t => t.TeamId);

            var gameParticipants = games
                .Where(g => fbsIds.Contains(g.HomeId ?? 0) || fbsIds.Contains(g.AwayId ?? 0))
                .SelectMany(g =>
                {
                    var homeId  = g.HomeId ?? 0;
                    var awayId  = g.AwayId ?? 0;
                    var homePts = g.HomePoints ?? 0;
                    var awayPts = g.AwayPoints ?? 0;
                    var neutral = g.NeutralSite == true;
                    var week    = g.Week;
                    return new[]
                    {
                        new GameParticipant
                        {
                            TeamId           = homeId,
                            TeamDivision     = teamById.TryGetValue(homeId, out var ht) ? ht.Division : "fbs",
                            OpponentId       = awayId,
                            OpponentDivision = teamById.TryGetValue(awayId, out var at) ? at.Division : "fbs",
                            TeamPoints       = homePts,
                            OpponentPoints   = awayPts,
                            Location         = neutral ? 'N' : 'W',
                            IsHomeTeam       = true,
                            Week             = week
                        },
                        new GameParticipant
                        {
                            TeamId           = awayId,
                            TeamDivision     = teamById.TryGetValue(awayId, out var at2) ? at2.Division : "fbs",
                            OpponentId       = homeId,
                            OpponentDivision = teamById.TryGetValue(homeId, out var ht2) ? ht2.Division : "fbs",
                            TeamPoints       = awayPts,
                            OpponentPoints   = homePts,
                            Location         = neutral ? 'N' : 'L',
                            IsHomeTeam       = false,
                            Week             = week
                        }
                    };
                })
                .ToList();

            // ── 5. Z-scores (composite, offensive, defensive) ─────────────────────
            //
            // Uses AvgScoreDifferential instead of AvgScoreDelta.
            // Expected margin derived from ExpandStrength(prior week Ranking) differential
            // — consistent with how the table was built in BuildAvgScoreDifferentialsAsync.
            // Falls back to raw win-pct differential if no prior ranking exists (week 1).
            var hfa = _config.HomeFieldAdvantage;
            double leagueAvgScore = games.Count > 0
                ? (games.Average(g => (double)(g.HomePoints ?? 0)) +
                   games.Average(g => (double)(g.AwayPoints ?? 0))) / 2.0
                : 28.0;

            var withZScores = gameParticipants.Select(gp =>
            {
                // Get pregame rankings from prior week snapshot.
                // Fall back to current win-pct if no prior snapshot (e.g. week 1).
                priorByTeamId.TryGetValue(gp.TeamId,     out var teamPrior);
                priorByTeamId.TryGetValue(gp.OpponentId, out var oppPrior);

                var teamStrength = RatingCalculator.ExpandStrength(teamPrior?.Ranking ?? 0m);
                var oppStrength  = RatingCalculator.ExpandStrength(oppPrior?.Ranking  ?? 0m);

                // Differential from team's perspective — positive means team is stronger.
                var rawDiff   = teamStrength - oppStrength;
                var clampedDiff = Math.Max(-3.0m, Math.Min(3.0m, rawDiff));
                var differential = Math.Round(clampedDiff / 0.05m, MidpointRounding.AwayFromZero) * 0.05m;

                var bucket = RatingCalculator.GetSmoothedExpectedMargin(avgScoreDifferentials, differential);

                double zScore = 0.0, offZScore = 0.0, defZScore = 0.0;

                // bucket is already from team's perspective (positive = team favored)
                var expectedFromTeam = (double)bucket;
                expectedFromTeam     = RatingCalculator.ApplyHomeField(
                    expectedFromTeam, gp.IsHomeTeam, gp.Location == 'N', hfa);

                var t1 = Math.Min(gp.TeamId, gp.OpponentId);
                var t2 = Math.Max(gp.TeamId, gp.OpponentId);
                var rivalryTier = matchupHistories.FirstOrDefault(
                    m => m.Team1Id == t1 && m.Team2Id == t2)?.RivalryTier;

                // Get StdDev from the differential bucket.
                var bucketRow = avgScoreDifferentials
                    .OrderBy(b => Math.Abs(b.StrengthDifferential - differential))
                    .FirstOrDefault();

                var effectiveStDev = bucketRow != null
                    ? (double)bucketRow.StdDevMargin * RatingCalculator.RivalryVarianceMultiplier(rivalryTier)
                    : 14.0 * RatingCalculator.RivalryVarianceMultiplier(rivalryTier);

                if (effectiveStDev > 0)
                {
                    var delta = gp.TeamPoints - gp.OpponentPoints;
                    zScore    = RatingCalculator.DampenZScore((delta - expectedFromTeam) / effectiveStDev);

                    var expectedTeamScore = leagueAvgScore + (expectedFromTeam / 2.0);
                    var expectedOppScore  = leagueAvgScore - (expectedFromTeam / 2.0);

                    offZScore = RatingCalculator.DampenZScore(
                        (gp.TeamPoints    - expectedTeamScore) / effectiveStDev);
                    defZScore = RatingCalculator.DampenZScore(
                        (expectedOppScore - gp.OpponentPoints) / effectiveStDev);
                }

                var divWeight = RatingCalculator.DivisionWeight(gp.OpponentDivision);

                return new
                {
                    gp.TeamId, gp.TeamDivision, gp.OpponentId, gp.OpponentDivision,
                    ZScore = zScore, OffZScore = offZScore, DefZScore = defZScore,
                    DivWeight = divWeight,
                    PerfWeight = zScore switch { >= 1.0 => 1.25, > -1.0 => 1.00, > -2.0 => 0.75, _ => 0.50 }
                };
            }).ToList();

            // ── 6-8. BaseSOS → SubSOS → CombinedSOS ──────────────────────────────
            var baseSOS = withZScores
                .GroupBy(x => x.TeamId)
                .ToDictionary(g => g.Key, g => Math.Round(
                    g.Sum(x => x.PerfWeight * x.DivWeight) / g.Sum(x => x.DivWeight), 3));

            var subSOS = withZScores
                .GroupBy(x => x.TeamId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.DivWeight) > 0
                    ? Math.Round(
                        g.Sum(x => baseSOS.GetValueOrDefault(x.OpponentId, 0.0) * x.PerfWeight) /
                        g.Sum(x => x.PerfWeight), 3)
                    : 0.0);

            var combinedSOS = fbsTeams.ToDictionary(t => t.TeamId, t =>
            {
                var b = baseSOS.GetValueOrDefault(t.TeamId, 0.0);
                var s = subSOS.GetValueOrDefault(t.TeamId, b);
                return Math.Round((2 * b + 3 * s) / 5.0, 4);
            });

            // ── 9. PowerRating ────────────────────────────────────────────────────
            var powerRatings = withZScores
                .GroupBy(x => x.TeamId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.DivWeight) > 0
                    ? Math.Round(
                        g.Sum(x => x.ZScore * x.DivWeight) / g.Sum(x => x.DivWeight) *
                        combinedSOS.GetValueOrDefault(g.Key, 1.0), 4)
                    : 0.0);

            // ── 10. Ranking ───────────────────────────────────────────────────────
            var rankings = fbsTeams.ToDictionary(t => t.TeamId, t =>
            {
                var wins   = winsLookup.GetValueOrDefault(t.TeamId, 0);
                var losses = lossesLookup.GetValueOrDefault(t.TeamId, 0);
                var total  = wins + losses;
                if (total == 0) return (decimal?)null;
                var winPct = (decimal)wins / total;
                var sos    = (decimal)combinedSOS.GetValueOrDefault(t.TeamId, 0.0);
                var pr     = (decimal)powerRatings.GetValueOrDefault(t.TeamId, 0.0);
                return (decimal?)Math.Round(winPct * sos * (1 + pr), 4);
            });

            // ── 11. Overall and tier ranks ────────────────────────────────────────
            var confLookup = await _uow.Conferences.GetDictionaryAsync(token);

            string ConfName(Teams t)
            {
                if (t.ConferenceId == null) return string.Empty;
                confLookup.TryGetValue(t.ConferenceId.Value, out var conf);
                return conf?.Name ?? string.Empty;
            }

            var ranked = fbsTeams
                .Where(t => rankings[t.TeamId].HasValue)
                .OrderByDescending(t => rankings[t.TeamId])
                .Select((t, i) => new
                {
                    Team        = t,
                    OverallRank = i + 1,
                    Tier        = RatingCalculator.GetConferenceTier(ConfName(t), t.TeamName)
                })
                .ToList();

            var tierRanks = new Dictionary<int, int>();
            foreach (var tierGroup in ranked.GroupBy(x => x.Tier))
            {
                int idx = 1;
                foreach (var x in tierGroup.OrderByDescending(x => rankings[x.Team.TeamId]))
                    tierRanks[x.Team.TeamId] = idx++;
            }

            // ── 12-13. Offensive / defensive Z-scores and ranks ───────────────────
            var offensiveZScores = withZScores.GroupBy(x => x.TeamId).ToDictionary(
                g => g.Key,
                g => g.Sum(x => x.DivWeight) > 0
                    ? Math.Round(g.Sum(x => x.OffZScore * x.DivWeight) / g.Sum(x => x.DivWeight), 4)
                    : 0.0);

            var defensiveZScores = withZScores.GroupBy(x => x.TeamId).ToDictionary(
                g => g.Key,
                g => g.Sum(x => x.DivWeight) > 0
                    ? Math.Round(g.Sum(x => x.DefZScore * x.DivWeight) / g.Sum(x => x.DivWeight), 4)
                    : 0.0);

            var fbsWithGames = fbsTeams
                .Where(t => (rawStats[t.TeamId][0] + rawStats[t.TeamId][1]) > 0).ToList();

            var offensiveRanks = fbsWithGames
                .OrderByDescending(t => offensiveZScores.GetValueOrDefault(t.TeamId, 0.0))
                .Select((t, i) => new { t.TeamId, Rank = i + 1 })
                .ToDictionary(x => x.TeamId, x => x.Rank);

            var defensiveRanks = fbsWithGames
                .OrderByDescending(t => defensiveZScores.GetValueOrDefault(t.TeamId, 0.0))
                .Select((t, i) => new { t.TeamId, Rank = i + 1 })
                .ToDictionary(x => x.TeamId, x => x.Rank);

            // ── 14. Upsert WeeklyRankings ─────────────────────────────────────────
            var existingRows   = await _uow.WeeklyRankings.GetByYearAndWeekAsync(year, week, token);
            var existingByTeam = existingRows.ToDictionary(r => r.TeamID);

            foreach (var t in fbsTeams)
            {
                var s           = rawStats[t.TeamId];
                var rank        = ranked.FirstOrDefault(r => r.Team.TeamId == t.TeamId);
                var gamesPlayed = s[0] + s[1];

                decimal avgPtsScored  = gamesPlayed > 0 ? Math.Round((decimal)s[2] / gamesPlayed, 2) : 0;
                decimal avgPtsAllowed = gamesPlayed > 0 ? Math.Round((decimal)s[3] / gamesPlayed, 2) : 0;
                decimal offZ          = gamesPlayed > 0 ? (decimal)Math.Round(offensiveZScores.GetValueOrDefault(t.TeamId, 0.0), 4) : 0;
                decimal defZ          = gamesPlayed > 0 ? (decimal)Math.Round(defensiveZScores.GetValueOrDefault(t.TeamId, 0.0), 4) : 0;
                int     offRank       = offensiveRanks.GetValueOrDefault(t.TeamId, 0);
                int     defRank       = defensiveRanks.GetValueOrDefault(t.TeamId, 0);

                if (existingByTeam.TryGetValue(t.TeamId, out var row))
                {
                    row.Wins             = (byte)s[0];
                    row.Losses           = (byte)s[1];
                    row.PointsFor        = s[2];
                    row.PointsAgainst    = s[3];
                    row.BaseSOS          = (decimal?)baseSOS.GetValueOrDefault(t.TeamId);
                    row.SubSOS           = (decimal?)subSOS.GetValueOrDefault(t.TeamId);
                    row.CombinedSOS      = (decimal?)combinedSOS.GetValueOrDefault(t.TeamId);
                    row.PowerRating      = (decimal?)powerRatings.GetValueOrDefault(t.TeamId);
                    row.Ranking          = rankings[t.TeamId];
                    row.OverallRank      = rank?.OverallRank ?? 0;
                    row.TierRank         = tierRanks.GetValueOrDefault(t.TeamId, 0);
                    row.AvgPointsScored  = avgPtsScored;
                    row.AvgPointsAllowed = avgPtsAllowed;
                    row.OffensiveZScore  = offZ;
                    row.DefensiveZScore  = defZ;
                    row.OffensiveRank    = offRank;
                    row.DefensiveRank    = defRank;
                }
                else
                {
                    await _uow.WeeklyRankings.AddAsync(new WeeklyRanking
                    {
                        TeamID           = t.TeamId,
                        Year             = (short)year,
                        Week             = (byte)week,
                        Wins             = (byte)s[0],
                        Losses           = (byte)s[1],
                        PointsFor        = s[2],
                        PointsAgainst    = s[3],
                        BaseSOS          = (decimal?)baseSOS.GetValueOrDefault(t.TeamId),
                        SubSOS           = (decimal?)subSOS.GetValueOrDefault(t.TeamId),
                        CombinedSOS      = (decimal?)combinedSOS.GetValueOrDefault(t.TeamId),
                        PowerRating      = (decimal?)powerRatings.GetValueOrDefault(t.TeamId),
                        Ranking          = rankings[t.TeamId],
                        OverallRank      = rank?.OverallRank ?? 0,
                        TierRank         = tierRanks.GetValueOrDefault(t.TeamId, 0),
                        AvgPointsScored  = avgPtsScored,
                        AvgPointsAllowed = avgPtsAllowed,
                        OffensiveZScore  = offZ,
                        DefensiveZScore  = defZ,
                        OffensiveRank    = offRank,
                        DefensiveRank    = defRank
                    }, token);
                }
            }

            await _uow.SaveChangesAsync(token);

            // ── 15. Sync TeamRecords from WeeklyRankings ──────────────────────────
            await _uow.TeamRecords.UpsertFromWeeklyRankingsAsync(year, token);

            // ── 16. Compute Seed/Trend/Pedigree rolling averages ──────────────────
            if (computeRollingAverages)
                await _rollingAverageService.ComputeAndPersistAsync(year, week, token);

            await _uow.SaveChangesAsync(token);

            // ── 17. Compute and persist projections for remaining unplayed games ───
            var remainingGames = allYearGames
                .Where(g => g.Week > week &&
                            (g.HomePoints ?? 0) == 0 &&
                            (g.AwayPoints ?? 0) == 0)
                .ToList();

            var matchupRequestsStep17 = remainingGames
                .Where(g => g.HomeId.HasValue && g.AwayId.HasValue &&
                            teamsDict.ContainsKey(g.HomeId.Value) &&
                            teamsDict.ContainsKey(g.AwayId.Value))
                .Select(g => new MatchupRequest
                {
                    TeamName     = teamsDict[g.HomeId!.Value].TeamName,
                    OpponentName = teamsDict[g.AwayId!.Value].TeamName,
                    Location     = g.NeutralSite == true ? 'N' : 'W',
                    Week         = g.Week
                })
                .ToList();

            if (matchupRequestsStep17.Count > 0)
            {
                var predictions = await _predictionService.PredictMatchups(year, matchupRequestsStep17, token);
                var projections = new List<Projection>(remainingGames.Count);

                foreach (var g in remainingGames)
                {
                    if (!g.HomeId.HasValue || !g.AwayId.HasValue) continue;
                    if (!teamsDict.TryGetValue(g.HomeId.Value,  out var homeTeam)) continue;
                    if (!teamsDict.TryGetValue(g.AwayId.Value,  out var awayTeam)) continue;

                    var pred = predictions.FirstOrDefault(p =>
                        p.TeamName     == homeTeam.TeamName &&
                        p.OpponentName == awayTeam.TeamName &&
                        p.Week         == g.Week);

                    if (pred == null) continue;

                    projections.Add(GamePredictionService.BuildProjection(
                        prediction: pred,
                        gameId:     g.GameId,
                        year:       year,
                        week:       week,
                        homeTeamId: g.HomeId.Value,
                        awayTeamId: g.AwayId.Value));
                }

                await _uow.Projections.UpsertManyAsync(projections, token);
            }
        }

        /// <summary>
        /// Backfills all weeks for a given year in chronological order.
        /// Runs rolling averages once at year end rather than per week.
        /// </summary>
        public async Task BackfillYearAsync(int year, CancellationToken token = default)
        {
            var allGames = await _uow.Games.GetByYearAsync(year, token);
            var weeks    = allGames
                .Select(g => g.Week)
                .Distinct()
                .OrderBy(w => w)
                .ToList();

            foreach (var week in weeks)
                await ComputeAndSaveAsync(year, week, token, computeRollingAverages: false);

            await _rollingAverageService.ComputeAndPersistAsync(year, null, token);
            await _uow.SaveChangesAsync(token);
        }
    }
}
