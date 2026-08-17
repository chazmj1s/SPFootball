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
    ///   Step 2     : simulate and persist a Projection for every game from
    ///                `week` through end of season, unconditionally — including
    ///                already-played games (needed so a historical backfill of
    ///                an already-completed season still gets a "predicted vs
    ///                actual" pregame projection for every game; real-vs-
    ///                projected resolution happens in step 3, not here). Keyed
    ///                at each game's own native week — Option C: a game gets at
    ///                most one Projection row, ever, locked at its own turn.
    ///   Steps 3-13 : compute all metrics in memory, reading real-or-projected
    ///                results through `week` via ResolvedGameResults (real
    ///                Games result always wins over a Projection when both exist)
    ///   Step 14    : write WeeklyRankings snapshot (authoritative)
    ///   Step 15    : UpsertFromWeeklyRankingsAsync → TeamRecords synced from WR
    ///   Step 16    : RollingAverageService → Seed/Trend/Pedigree (skippable for backfill)
    ///
    /// Z-score pipeline now uses AvgScoreDifferential (replaces AvgScoreDelta):
    ///   - Expected margin derived from ExpandStrength(prior week Ranking) differential
    ///   - StdDev from AvgScoreDifferential.StdDevMargin
    ///   - Consistent with the prediction engine and the table's own construction
    ///
    /// For years with unplayed games (e.g. a future season), projected scores are
    /// substituted for missing actuals so the full pipeline can run meaningfully.
    /// Synthetic scores are never written to the Games table — they're persisted
    /// to Projections only, and resolved against real Games via the
    /// ResolvedGameResults view. WeeklyRankings and TeamRecords are overwritten by
    /// actuals as the season progresses, since the entire table can be rebuilt
    /// from scratch at any time — WeeklyRankings itself doesn't track or care
    /// whether a given week's numbers came from real or projected results.
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
        private readonly ConferenceTierService  _tierService;

        public WeeklyRankingsService(
            IUnitOfWork uow,
            GamePredictionService predictionService,
            RollingAverageService rollingAverageService,
            ConferenceTierService tierService,
            IOptions<MetricsConfiguration> config)
        {
            _uow                   = uow;
            _config                = config.Value;
            _predictionService     = predictionService;
            _rollingAverageService = rollingAverageService;
            _tierService           = tierService;
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

            // SeedRating fallback for opponent pregame strength — used when no
            // prior WeeklyRankings row exists (e.g. week 1 of a new season, or
            // when a team hasn't been ranked yet). Lives on TeamRecords from
            // RollingAverageService's offseason calc.
            var currentYearRecordsForSeed = await _uow.TeamRecords.GetByYearAsync(year, token);
            var seedByTeamId = currentYearRecordsForSeed
                .Where(tr => tr.SeedRating.HasValue)
                .ToDictionary(tr => tr.TeamID, tr => tr.SeedRating!.Value);

            // Year-aware tier data — hoisted here (was previously loaded later, at
            // step 11's overall/tier-rank computation) so the SAME dictionary can
            // also drive the tier-discount lookup in the projection loop below,
            // rather than querying it twice for the same year.
            var tierByTeamId = await _tierService.GetConfDataBatchAsync(fbsIds, year, token);
            string TierFor(int teamId, string teamName) =>
                tierByTeamId.TryGetValue(teamId, out var cd)
                    ? cd.Tier
                    : ConferenceTierService.GetTierStatic(null, teamName);

            // Tier discount coefficient for this season — see TierDiscountCoefficient
            // remarks. Null if RunSeasonSetupAsync's compute step hasn't run yet for
            // this season; BuildProjection treats a null coefficient the same as a
            // same-tier matchup (no adjustment applied).
            var tierDiscountCoefficient = await _uow.TierDiscountCoefficients
                .GetLatestBySeasonAsync(year, token);

            // ── 2. Decide/refresh every game from `week` through end of season ─────
            //
            // REBUILT (Option C) — replaces the old steps 2b + 17, which did the
            // same underlying job (predict games that haven't happened) at two
            // different scopes and offsets: 2b substituted only THIS week's
            // still-unplayed games, transiently — never persisted, thrown away at
            // the end of this method. 17 separately projected only the REMAINING
            // season (g.Week > week), persisted, but keyed at the current run's
            // week rather than each game's own week — a game accumulated a new
            // row on every single weekly run, an intentional "one snapshot per
            // as-of-week" design that's been replaced.
            //
            // New design: every game from `week` through the end of the season
            // gets (re)decided in ONE pass, off ONE rating snapshot — the most
            // recent LOCKED week (useWeekAsLive: false, the default — this
            // week's own WeeklyRankings row doesn't exist yet; it's what THIS run
            // is about to build below, in step 3). Each prediction is persisted
            // keyed at THAT GAME'S OWN native week, not the current run's week.
            // Under this scheme a game gets at most ONE Projection row, ever:
            // every run where week <= that game's own week includes it and
            // overwrites the same row; once the game's own week has passed, no
            // future run's g.Week >= week filter includes it again, so the row
            // made at its own turn is permanently locked. That single write both
            // decides THIS week's own game (for step 3 below to read back
            // immediately, via ResolvedGameResults) and refreshes every future
            // week's projection in the same pass.
            var allYearGames = await _uow.Games.GetByYearAsync(year, token);
            var teamsDict    = await _uow.Teams.GetDictionaryByTeamIdAsync(token);

            // Note: deliberately NOT gated on "unplayed" (HomePoints/AwayPoints
            // == 0). g.Week >= week alone is what makes the lock work — once
            // `week` passes a game's own native week, no future run's filter
            // includes it again, regardless of whether Games has a real score
            // by then. Generating a projection and USING one are separate
            // concerns: ResolvedGameResults already prefers the real Games
            // result over a Projection whenever both exist, so writing a
            // projection for an already-played game costs nothing for live
            // calc correctness — and it's required for a historical backfill
            // (e.g. a fully-completed past season, where every game already
            // has a real score from the very first run): without this, the
            // old "only unplayed" gate meant gamesToProject was permanently
            // empty for such a season and no pregame projection ever got
            // written at all, breaking "predicted vs actual" display for
            // every already-played game.
            var gamesToProject = allYearGames
                .Where(g => g.Week >= week &&
                            g.HomeId.HasValue && g.AwayId.HasValue &&
                            teamsDict.ContainsKey(g.HomeId.Value) &&
                            teamsDict.ContainsKey(g.AwayId.Value))
                .ToList();

            if (gamesToProject.Count > 0)
            {
                // Brand-new season, week 1, before any TeamRecords exist yet for
                // `year` — fall back to last year's so the rating blend has
                // something to anchor from. Same guard the old step 2b used.
                var currentYearRecords = await _uow.TeamRecords.GetByYearAsync(year, token);
                var predictionYear     = currentYearRecords.Any() ? year : year - 1;

                var matchupRequests = gamesToProject
                    .Select(g => new MatchupRequest
                    {
                        TeamName     = teamsDict[g.HomeId!.Value].TeamName,
                        OpponentName = teamsDict[g.AwayId!.Value].TeamName,
                        Location     = g.NeutralSite == true ? 'N' : 'H',
                        Week         = g.Week
                    })
                    .ToList();

                // useWeekAsLive: false (default) — none of these games have
                // happened yet as of this run; rating source is the most recent
                // LOCKED week, exactly like the old PredictMatchup / step-2b
                // pattern. Finding #1's useWeekAsLive: true no longer has a
                // caller under this design — the only place that used it (old
                // step 17) is what this block replaces.
                var predictions = await _predictionService.PredictMatchups(
                    predictionYear, week, matchupRequests, token);

                var projectionsToWrite = new List<Projection>(gamesToProject.Count);

                foreach (var g in gamesToProject)
                {
                    if (!g.HomeId.HasValue || !g.AwayId.HasValue) continue;
                    if (!teamsDict.TryGetValue(g.HomeId.Value, out var homeTeam)) continue;
                    if (!teamsDict.TryGetValue(g.AwayId.Value, out var awayTeam)) continue;

                    var pred = predictions.FirstOrDefault(p =>
                        p.TeamName     == homeTeam.TeamName &&
                        p.OpponentName == awayTeam.TeamName &&
                        p.Week         == g.Week);

                    if (pred == null) continue;

                    // Week: g.Week — the game's OWN native week, not the current
                    // run's week. This is what makes the lock work; see remarks
                    // above. Tie-breaking, integer scores, and spread/total
                    // consistency are all handled inside BuildProjection now —
                    // no separate 0-0 guard needed here like the old step 2b had.
                    //
                    // homeWinDiff/awayWinDiff: real Wins - Losses from the SAME
                    // priorByTeamId snapshot the rating pipeline itself reads —
                    // same "at kickoff" convention TierDiscountCalculator uses.
                    // A team absent from priorByTeamId (no games yet) defaults to
                    // 0-0, not skipped — a legitimate real record, not missing data.
                    var homeWinDiff = priorByTeamId.TryGetValue(g.HomeId.Value, out var hpr)
                        ? hpr.Wins - hpr.Losses : 0;
                    var awayWinDiff = priorByTeamId.TryGetValue(g.AwayId.Value, out var apr)
                        ? apr.Wins - apr.Losses : 0;

                    projectionsToWrite.Add(GamePredictionService.BuildProjection(
                        prediction: pred,
                        gameId:     g.GameId,
                        year:       year,
                        week:       g.Week,
                        homeTeamId: g.HomeId.Value,
                        awayTeamId: g.AwayId.Value,
                        homeWinDiff: homeWinDiff,
                        awayWinDiff: awayWinDiff,
                        homeTier:   TierFor(homeTeam.TeamId, homeTeam.TeamName),
                        awayTier:   TierFor(awayTeam.TeamId, awayTeam.TeamName),
                        tierDiscountCoefficient: tierDiscountCoefficient));
                }

                await _uow.Projections.UpsertManyAsync(projectionsToWrite, token);
            }

            // Resolved (real-or-projected) results through `week`, for this
            // team's cumulative history. Replaces the old Games-only fetch +
            // transient substitution — reads straight from ResolvedGameResults
            // (real result if played, else the locked Projection row — see
            // ResolvedGameResult remarks), and already includes the rows just
            // written above for this week's own games.
            var resolvedGames = await _uow.ResolvedGameResults.GetByYearThroughWeekAsync(year, week, token);

            // ── 3. Raw stats per team [wins, losses, pf, pa] ──────────────────────
            var rawStats = fbsTeams.ToDictionary(t => t.TeamId, _ => new int[4]);

            foreach (var g in resolvedGames)
            {
                var homeId   = g.HomeId ?? 0;
                var awayId   = g.AwayId ?? 0;
                var homePts  = g.HomePoints;
                var awayPts  = g.AwayPoints;
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

            var gameParticipants = resolvedGames
                .Where(g => fbsIds.Contains(g.HomeId ?? 0) || fbsIds.Contains(g.AwayId ?? 0))
                .SelectMany(g =>
                {
                    var homeId  = g.HomeId ?? 0;
                    var awayId  = g.AwayId ?? 0;
                    var homePts = g.HomePoints;
                    var awayPts = g.AwayPoints;
                    var neutral = g.NeutralSite;
                    var gWeek   = g.Week;
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
                            Location         = neutral ? 'N' : 'H',
                            IsHomeTeam       = true,
                            Week             = gWeek
                        },
                        new GameParticipant
                        {
                            TeamId           = awayId,
                            TeamDivision     = teamById.TryGetValue(awayId, out var at2) ? at2.Division : "fbs",
                            OpponentId       = homeId,
                            OpponentDivision = teamById.TryGetValue(homeId, out var ht2) ? ht2.Division : "fbs",
                            TeamPoints       = awayPts,
                            OpponentPoints   = homePts,
                            Location         = neutral ? 'N' : 'A',
                            IsHomeTeam       = false,
                            Week             = gWeek
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
            double leagueAvgScore = resolvedGames.Count > 0
                ? (resolvedGames.Average(g => (double)g.HomePoints) +
                   resolvedGames.Average(g => (double)g.AwayPoints)) / 2.0
                : 28.0;

            // Ranking = winPct × (1 + PowerRating) is genuinely 0/undefined for
            // any team with zero wins — not just week 1 of a new season, but
            // also a team on a winless in-season stretch (0-1, 0-2, 0-3...).
            // Treating that 0 as "this team has 0 strength" silently makes such
            // a team look talent-blind for the margin/z-score calc below.
            //
            // PowerRating, unlike Ranking, updates from Z-scores every week
            // regardless of win/loss outcome — it's the signal that actually
            // exists before (or without) any wins. Reconstructing an effective
            // Ranking as if the team were exactly .500 — 0.5 * (1 + PowerRating)
            // — keeps the result in the same numeric range real Ranking values
            // occupy (the bucket table's StrengthDifferential is calibrated
            // against that range), while still reflecting real, per-week-updating
            // separation instead of a frozen fallback.
            //
            // Falls back to the raw preseason SeedRating (already on the
            // Ranking scale, not the PowerRating scale) only when there's no
            // prior WeeklyRankings row at all to read a PowerRating from.
            var withZScores = gameParticipants.Select(gp =>
            {
                // Get pregame rankings from prior week snapshot.
                // Fall back to current win-pct if no prior snapshot (e.g. week 1).
                priorByTeamId.TryGetValue(gp.TeamId,     out var teamPrior);
                priorByTeamId.TryGetValue(gp.OpponentId, out var oppPrior);

                var teamStrength = RatingCalculator.ExpandStrength(RatingCalculator.ResolveStrength(gp.TeamId, teamPrior, seedByTeamId));
                var oppStrength = RatingCalculator.ExpandStrength(RatingCalculator.ResolveStrength(gp.OpponentId, oppPrior, seedByTeamId));
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
                var matchup = matchupHistories.FirstOrDefault(
                    m => m.Team1Id == t1 && m.Team2Id == t2);

                // Get StdDev from the differential bucket.
                var bucketRow = avgScoreDifferentials
                    .OrderBy(b => Math.Abs(b.StrengthDifferential - differential))
                    .FirstOrDefault();

                var baseStdDev = bucketRow != null ? (double)bucketRow.StdDevMargin : 14.0;
                var effectiveStDev = baseStdDev * RatingCalculator.RivalryVarianceMultiplier(matchup, baseStdDev);

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

                // Smooth quality-of-win modifier — replaces the four-bucket step.
                //   QualityMod = clamp(1 + z * 0.25, 0.50, 1.50)
                // Applied to the team's own z-score in PowerRating, NOT to SOS.
                var qualityMod = Math.Max(0.50, Math.Min(1.50, 1.0 + (zScore * 0.25)));

                // Pregame opponent strength for the new SOS calc.
                // Chain: WeeklyRankings[opponent, week-1].Ranking → SeedRating → 0
                // FCS opponents (and any opponent we can't find) get 0 strength.
                decimal oppPregameStrength = 0m;
                bool oppIsFcs = string.Equals(gp.OpponentDivision, "fcs",
                                    StringComparison.OrdinalIgnoreCase);
                if (!oppIsFcs)
                    oppPregameStrength = RatingCalculator.ResolveStrength(gp.OpponentId, oppPrior, seedByTeamId);

                return new
                {
                    gp.TeamId, gp.TeamDivision, gp.OpponentId, gp.OpponentDivision,
                    ZScore = zScore, OffZScore = offZScore, DefZScore = defZScore,
                    DivWeight = divWeight,
                    QualityMod = qualityMod,
                    OppStrength = (double)oppPregameStrength
                };
            }).ToList();

            // ── 6. Full-season opponent set for SOS ────────────────────────────────
            //
            // BaseSOS/SubSOS now reflect the team's FULL schedule, not just games
            // played/locked through this week — consistent with how projected
            // Wins/Losses are already tracked as a blend of played games and
            // projections elsewhere (BuildActualRecordRollup/BuildProjectedRecord-
            // Rollup). ResolvedGameResults.GetByYearAsync (real result if played,
            // else the locked Projection — same resolution GetByYearThroughWeekAsync
            // above already uses, just unscoped by week) gives this for free: no new
            // fallback logic, no separate "is this week 0" branch. At week 0 this is
            // 100% projected; as the season plays out, real results replace
            // projections week by week, same mechanism, same code path throughout.
            //
            // Deliberately NOT reused for rawStats/Z-scores above — those grade
            // actual, resolved performance and must stay "through week" (grading a
            // team's Z-score against its own not-yet-played, still-projected games
            // would be circular). Only opponent-strength/SOS widens to the full
            // schedule; Wins/Losses/PointsFor/PointsAgainst/OffensiveZScore/
            // DefensiveZScore below are untouched.
            var fullSeasonGames = await _uow.ResolvedGameResults.GetByYearAsync(year, token);

            var sosParticipants = fullSeasonGames
                .Where(g => fbsIds.Contains(g.HomeId ?? 0) || fbsIds.Contains(g.AwayId ?? 0))
                .SelectMany(g =>
                {
                    var homeId = g.HomeId ?? 0;
                    var awayId = g.AwayId ?? 0;
                    return new[]
                    {
                        new
                        {
                            TeamId           = homeId,
                            OpponentId       = awayId,
                            OpponentDivision = teamById.TryGetValue(awayId, out var at)
                                ? at.Division : "fbs"
                        },
                        new
                        {
                            TeamId           = awayId,
                            OpponentId       = homeId,
                            OpponentDivision = teamById.TryGetValue(homeId, out var ht)
                                ? ht.Division : "fbs"
                        }
                    };
                })
                .Select(p =>
                {
                    priorByTeamId.TryGetValue(p.OpponentId, out var oppPrior);
                    bool oppIsFcs = string.Equals(p.OpponentDivision, "fcs",
                                        StringComparison.OrdinalIgnoreCase);
                    decimal oppPregameStrength = oppIsFcs
                        ? 0m
                        : RatingCalculator.ResolveStrength(p.OpponentId, oppPrior, seedByTeamId);

                    return new
                    {
                        p.TeamId,
                        p.OpponentId,
                        DivWeight = RatingCalculator.DivisionWeight(p.OpponentDivision),
                        OppStrength = (double)oppPregameStrength
                    };
                })
                .ToList();

            // ── 6-8. BaseSOS → SubSOS → CombinedSOS ──────────────────────────────
            //
            // CORRECTED: SOS is now a pure opponent-strength metric.
            //   BaseSOS  = weighted average of opponents' pregame strength
            //              (Ranking from prior week, fallback to SeedRating, 0 for FCS)
            //              weighted by division weight
            //   SubSOS   = weighted average of opponents' BaseSOS (same weighting)
            //   CombinedSOS = (2 * BaseSOS + 3 * SubSOS) / 5
            //
            // The old performance-weight bucketing has moved to PowerRating below as
            // a smooth QualityMod applied to the team's own z-score.
            var baseSOS = sosParticipants
                .GroupBy(x => x.TeamId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.DivWeight) > 0
                    ? Math.Round(
                        g.Sum(x => x.OppStrength * x.DivWeight) / g.Sum(x => x.DivWeight), 4)
                    : 0.0);

            var subSOS = sosParticipants
                .GroupBy(x => x.TeamId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.DivWeight) > 0
                    ? Math.Round(
                        g.Sum(x => baseSOS.GetValueOrDefault(x.OpponentId, 0.0) * x.DivWeight) /
                        g.Sum(x => x.DivWeight), 4)
                    : 0.0);

            var combinedSOS = fbsTeams.ToDictionary(t => t.TeamId, t =>
            {
                var b = baseSOS.GetValueOrDefault(t.TeamId, 0.0);
                var s = subSOS.GetValueOrDefault(t.TeamId, b);
                return Math.Round((2 * b + 3 * s) / 5.0, 4);
            });

            // ── 9. PowerRating ────────────────────────────────────────────────────
            //
            // CORRECTED: Quality-of-win lives here, not in SOS.
            //   AvgZScore   = weighted avg of (ZScore * QualityMod), weighted by DivWeight
            //   PowerRating = AvgZScore * CombinedSOS
            //
            // A team's z-score is scaled by how decisively they over/under-performed
            // expectations (the smooth QualityMod) BEFORE averaging. SOS then scales
            // the whole thing for opponent quality.
            var powerRatings = withZScores
                .GroupBy(x => x.TeamId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.DivWeight) > 0
                    ? Math.Round(
                        g.Sum(x => x.ZScore * x.QualityMod * x.DivWeight) /
                        g.Sum(x => x.DivWeight) *
                        combinedSOS.GetValueOrDefault(g.Key, 1.0), 4)
                    : 0.0);

            // ── 10. Ranking ───────────────────────────────────────────────────────
            //
            // CORRECTED: SOS removed from the Ranking formula.
            //
            //   OLD: Ranking = WinPct × CombinedSOS × (1 + PowerRating)
            //   NEW: Ranking = WinPct × (1 + PowerRating)
            //
            // Why: Ranking is read back as opponent pregame strength in the NEXT
            // week's SOS calculation. With CombinedSOS multiplied INTO Ranking,
            // SOS compounded against itself week-over-week — values geometrically
            // collapsed toward zero. The old formula tolerated it because the
            // buggy SOS clustered tightly around 1.0 (effectively a no-op).
            //
            // PowerRating already contains SOS via AvgZScore × CombinedSOS, so
            // SOS is still represented in Ranking via PowerRating — just not
            // double-counted in a way that creates a feedback loop.
            //
            // This also aligns with TeamMetricsService.CalculateRankings, which
            // already uses this formula.
            var rankings = fbsTeams.ToDictionary(t => t.TeamId, t =>
            {
                var wins   = winsLookup.GetValueOrDefault(t.TeamId, 0);
                var losses = lossesLookup.GetValueOrDefault(t.TeamId, 0);
                var total  = wins + losses;
                if (total == 0) return (decimal?)null;
                var winPct = (decimal)wins / total;
                var pr     = (decimal)powerRatings.GetValueOrDefault(t.TeamId, 0.0);
                return (decimal?)Math.Round(winPct * (1 + pr), 4);
            });

            // ── 11. Overall and tier ranks ────────────────────────────────────────
            // tierByTeamId/TierFor already loaded in step 1 above — reused here
            // rather than querying GetConfDataBatchAsync a second time for the same
            // year.
            var ranked = fbsTeams
                .Where(t => rankings[t.TeamId].HasValue)
                .OrderByDescending(t => rankings[t.TeamId])
                .Select((t, i) => new
                {
                    Team        = t,
                    OverallRank = i + 1,
                    Tier        = TierFor(t.TeamId, t.TeamName)
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
