using SaturdayPulse.Api.Extensions;
using SaturdayPulse.Contracts;
using SaturdayPulse.Models;

namespace SaturdayPulse.Utilities
{
    /// <summary>
    /// G6/P4 discount coefficient analysis. Originally three validation methods (see
    /// "G6 discount algorithm" doc), then a fourth measurement added alongside them;
    /// reduced down to this one over the course of that investigation:
    ///
    ///   - Method A (Cross-Tier Baseline Control Test) — REMOVED. The vast majority of
    ///     its weighted volume ended up clamped against the edge of its own control
    ///     curve rather than genuinely interpolated; its output wasn't trustworthy.
    ///   - Method C (Closed Loop Variance Cap) — REMOVED as a standalone calculation.
    ///     Its rolling-window output showed the underlying ratio drifting meaningfully
    ///     across eras (~0.95 to ~1.18), so a single all-time value from it was never
    ///     going to be the right thing to persist. Its real finding — that this
    ///     relationship is NOT static and will keep changing over time — is acted on
    ///     structurally instead: this calculator is re-run once per season (see
    ///     RunSeasonSetupAsync), using only data through the prior year, so the
    ///     persisted coefficients drift with reality automatically rather than needing
    ///     a second calculation to track drift on the side.
    ///   - Production Comparison — REMOVED as a standalone measurement after serving
    ///     its purpose: it measured this same two-parameter model against the REAL,
    ///     live IAvgScoreDifferentialService output instead of a synthetic curve, and
    ///     confirmed Method B's numbers (D and C landed within ~2% of each other across
    ///     two structurally different models run against the identical 5,505-game
    ///     population). Decision made to apply Method B's output directly rather than
    ///     Production's — both were statistically defensible; the margin between tiers
    ///     is real and directly observable, and Method B is the simpler of the two
    ///     models to reason about and maintain going forward.
    ///   - Method B (MOV Variance Test, two-parameter) — the one that's actually
    ///     applied. Fits a discount D and a flat caliber-gap constant C. See remarks
    ///     below for the full method.
    ///
    /// Shared infrastructure:
    ///   - Games: regular-season only, both teams FBS, from GetPlayedGamesSinceYearAsync.
    ///   - Win differential ("at kickoff") sourced from WeeklyRankings at Week - 1 for
    ///     each team/year — NOT TeamRecord (a live rolling value) and NOT tallied from
    ///     Games directly. WeeklyRankings.Week=N reflects each team's record AFTER week
    ///     N's games are final, so a game played in week N joins to week N-1's snapshot
    ///     to avoid folding that game's own result into its own "at kickoff" value.
    ///     Week-1 games join to the Week 0 (preseason) snapshot populated by
    ///     InitializeSeasonAsync; a year missing that snapshot has every Week-1 game
    ///     excluded (surfaced via GamesSkippedNoPriorWeekSnapshot).
    ///   - Tier classification: each team's conference is resolved PER YEAR via
    ///     TeamsConferenceHistory (bulk-loaded once per distinct year, via
    ///     GetConferenceIdsByYearAsync) joined against Conferences (loaded once —
    ///     conference definitions don't vary by year), then run through
    ///     TeamTierEvaluatorExtensions.IsTierOne(seasonYear, resolvedAbbreviation).
    ///     This does NOT use Teams.Conference (the static current-conference FK) — see
    ///     TeamTierEvaluatorExtensions remarks for why that was a real bug. A team with
    ///     no TeamsConferenceHistory row for a given year is excluded, not defaulted to
    ///     Tier 2 (surfaced via GamesSkippedNoConferenceHistory).
    ///
    ///   Note this remains a SEPARATE, parallel system from ConferenceTierService (the
    ///   project's established P4/G6 classification service) rather than routing
    ///   through it — still an open follow-up, not resolved here.
    ///
    /// ── Method B — MOV Variance Test (two-parameter) ────────────────────────────────
    /// TIER-oriented (Tier1Team - Tier2Team), not record-oriented — discounts the
    /// TIER 2 team's win differential input specifically, regardless of which team has
    /// the better raw record in a given matchup. Fits Predicted_g(D, C) = a continuous,
    /// odd-symmetric mirror of a Tier1-vs-Tier1-only curve (see BuildSignedAnchorPoints
    /// remarks — a naive "look up a magnitude, then reapply Math.Sign" approach was
    /// tried first and had a real discontinuity at every tied-nonzero-record game's
    /// exact D=WinDiffT1/WinDiffT2 threshold) evaluated at (WinDiffT1 - D*WinDiffT2),
    /// PLUS a flat additive constant C.
    ///
    /// Matching only the population average cannot pin down two parameters — C can
    /// absorb whatever the mean error is for ANY D, so infinitely many (D, C) pairs
    /// would satisfy an average-only match equally well. This is a real per-game
    /// least-squares fit instead: for a fixed D, the optimal C has a closed form (the
    /// mean of the per-game residuals at that D), which collapses the two-parameter
    /// problem to a 1D grid search over D alone — minimizing the SUM OF SQUARED
    /// per-game errors (the residual variance), not the mean error, since the mean is
    /// trivially zeroed out by C regardless of D.
    ///
    /// BaselineError = mean(ActualDelta) - mean(UndiscountedPredictedDelta) at D=1,
    /// C=0 — this dataset's own empirical analog to the doc's externally-asserted
    /// "10 to 14 points" structural margin, NOT hardcoded to that range, since it
    /// should come from our own data rather than an imported figure.
    /// </summary>
    public class TierDiscountCalculator(IUnitOfWork _uow)
    {
        // Minimum sample size for a Curve 1 bucket to be used as an interpolation
        // anchor. Buckets below this are excluded from the lookup — a 1-2 game bucket
        // at the noisy tail can break monotonicity badly enough to hijack
        // interpolation for every bucket above it.
        private const int MinAnchorSampleSize = 20;

        // Bounded grid search range/step — Curve 1 is real, potentially noisy data, so
        // a closed-form or bisection solve isn't assumed safe.
        private const double SearchMin = -1.0;
        private const double SearchMax = 3.0;
        private const double SearchStep = 0.001;

        private static readonly double[] CheckpointValues =
            { -1.0, -0.5, 0.0, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0 };

        /// <summary>
        /// throughYear caps which games are included (Year &lt;= throughYear) — critical
        /// for backfilling past seasons' coefficients: without a cap, every backfilled
        /// season would pull in every game up through TODAY, making every historical
        /// row identical and defeating the entire point of tracking how D/C drift over
        /// time. Live/current-season callers can omit it (null = no cap), since there's
        /// naturally no future data yet at the point this runs each season.
        /// </summary>
        public async Task<TierDiscountAnalysisResult> CalculateAsync(
            int startYear = 1965, int? throughYear = null, CancellationToken token = default)
        {
            var teams = await _uow.Teams.GetAllAsync(token);
            var teamsById = teams.ToDictionary(t => t.TeamId);

            var fbsTeamIds = teams
                .Where(t => string.Equals(t.Division, "fbs", StringComparison.OrdinalIgnoreCase))
                .Select(t => t.TeamId)
                .ToHashSet();

            var allGames = await _uow.Games.GetPlayedGamesSinceYearAsync(startYear, token);

            var games = allGames
                .Where(g => string.Equals(g.SeasonType, "regular", StringComparison.OrdinalIgnoreCase))
                .Where(g => g.HomeId.HasValue && g.AwayId.HasValue &&
                            g.HomePoints.HasValue && g.AwayPoints.HasValue)
                .Where(g => fbsTeamIds.Contains(g.HomeId!.Value) && fbsTeamIds.Contains(g.AwayId!.Value))
                .Where(g => throughYear == null || g.Year <= throughYear.Value)
                .ToList();

            // Bulk-load WeeklyRankings per distinct year — avoids one query per game.
            var years = games.Select(g => g.Year).Distinct().OrderBy(y => y).ToList();
            var snapshotsByYear = new Dictionary<int, Dictionary<(int TeamId, int Week), WeeklyRanking>>();
            foreach (var year in years)
            {
                var rows = await _uow.WeeklyRankings.GetByYearAsync(year, token);
                snapshotsByYear[year] = rows.ToDictionary(r => (r.TeamID, (int)r.Week), r => r);
            }

            // Conference definitions (Id -> row) don't vary by year — one call, not one
            // per year. Per-year TEAM membership does vary and is bulk-loaded below, one
            // call per distinct year via GetConferenceIdsByYearAsync (TeamsConferenceHistory).
            var confById = await _uow.Conferences.GetDictionaryAsync(token);

            var conferenceIdByTeamIdByYear = new Dictionary<int, Dictionary<int, int>>();
            foreach (var year in years)
            {
                conferenceIdByTeamIdByYear[year] =
                    await _uow.TeamsConferenceHistory.GetConferenceIdsByYearAsync(year, token);
            }

            string? ResolveConferenceAbbreviation(int teamId, int year)
            {
                if (!conferenceIdByTeamIdByYear.TryGetValue(year, out var confIdByTeamId)) return null;
                if (!confIdByTeamId.TryGetValue(teamId, out var conferenceId)) return null;
                return confById.TryGetValue(conferenceId, out var conference) ? conference.Abbreviation : null;
            }

            var result = new TierDiscountAnalysisResult { StartYear = startYear };

            // ── Shared per-game pass: build Curve 1 AND the cross-tier game list. ──
            var controlRaw = new Dictionary<int, List<double>>(); // Curve 1 (Tier1-vs-Tier1 only)
            var methodBGames = new List<(double WinDiffT1, double WinDiffT2, double ScoreT1, double ScoreT2)>();

            int skippedNoConferenceHistory = 0;
            int skippedNoPriorWeekSnapshot = 0;

            foreach (var g in games)
            {
                if (!teamsById.TryGetValue(g.HomeId!.Value, out var homeTeam) ||
                    !teamsById.TryGetValue(g.AwayId!.Value, out var awayTeam))
                {
                    continue;
                }

                var homeConferenceAbbr = ResolveConferenceAbbreviation(homeTeam.TeamId, g.Year);
                var awayConferenceAbbr = ResolveConferenceAbbreviation(awayTeam.TeamId, g.Year);

                if (homeConferenceAbbr == null || awayConferenceAbbr == null)
                {
                    skippedNoConferenceHistory++;
                    continue;
                }

                var snapshots = snapshotsByYear[g.Year];
                var priorWeek = g.Week - 1;

                if (!snapshots.TryGetValue((homeTeam.TeamId, priorWeek), out var homeSnap) ||
                    !snapshots.TryGetValue((awayTeam.TeamId, priorWeek), out var awaySnap))
                {
                    skippedNoPriorWeekSnapshot++;
                    continue;
                }

                var homeWinDiff = homeSnap.Wins - homeSnap.Losses;
                var awayWinDiff = awaySnap.Wins - awaySnap.Losses;

                var homeTier1 = homeTeam.IsTierOne(g.Year, homeConferenceAbbr);
                var awayTier1 = awayTeam.IsTierOne(g.Year, awayConferenceAbbr);

                if (homeTier1 && awayTier1)
                {
                    // Curve 1 — Control Baseline: fully symmetric, absolute both axes.
                    // Order doesn't matter here — pure "raw record-gap magnitude to
                    // score-gap magnitude" baseline.
                    var bucketKey = Math.Abs(homeWinDiff - awayWinDiff);
                    var value = Math.Abs((double)(g.HomePoints!.Value - g.AwayPoints!.Value));

                    if (!controlRaw.TryGetValue(bucketKey, out var list))
                        controlRaw[bucketKey] = list = new List<double>();
                    list.Add(value);
                }
                else if (homeTier1 != awayTier1)
                {
                    // TIER-oriented (not record-oriented) — which team is "T1"/"T2" is
                    // fixed by actual tier, not by which has the better record.
                    double winDiffT1, winDiffT2, scoreT1, scoreT2;
                    if (homeTier1)
                    {
                        winDiffT1 = homeWinDiff; winDiffT2 = awayWinDiff;
                        scoreT1 = g.HomePoints!.Value; scoreT2 = g.AwayPoints!.Value;
                    }
                    else
                    {
                        winDiffT1 = awayWinDiff; winDiffT2 = homeWinDiff;
                        scoreT1 = g.AwayPoints!.Value; scoreT2 = g.HomePoints!.Value;
                    }

                    methodBGames.Add((winDiffT1, winDiffT2, scoreT1, scoreT2));
                }
                // both false (Tier 2 vs Tier 2) — not relevant to this measurement.
            }

            result.GamesSkippedNoConferenceHistory = skippedNoConferenceHistory;
            result.GamesSkippedNoPriorWeekSnapshot = skippedNoPriorWeekSnapshot;

            // ── Curve 1 ───────────────────────────────────────────────────────────
            var controlCurve = controlRaw
                .Select(kvp => new TierCurvePoint
                {
                    WinDifferential = kvp.Key,
                    AvgScoreDelta = Math.Round(kvp.Value.Average(), 4),
                    SampleSize = kvp.Value.Count
                })
                .OrderBy(p => p.WinDifferential)
                .ToList();

            var anchorPoints = controlCurve
                .Where(p => p.SampleSize >= MinAnchorSampleSize)
                .OrderBy(p => p.WinDifferential)
                .ToList();

            var signedAnchorPoints = BuildSignedAnchorPoints(anchorPoints);

            if (anchorPoints.Count == 0)
            {
                // Every prediction would collapse to 0 (BuildSignedAnchorPoints still
                // returns the forced (0,0) origin point, so PredictScoreDeltaSigned
                // never throws — it just always returns 0). With predictNoC constant
                // across every d, FitTwoParameterModel's grid search would report
                // whatever d it tries first as "solved" — not a real fit. Most likely
                // to occur for the earliest seasons of a full historical backfill,
                // where the Tier1-vs-Tier1 population in that limited window is too
                // thin for any bucket to reach MinAnchorSampleSize.
                result.MethodB = new TwoParameterFitResult
                {
                    GamesUsed = methodBGames.Count,
                    ExclusionReason = $"No Curve 1 bucket meets the minimum sample size ({MinAnchorSampleSize}) — cannot fit against an empty control curve"
                };
            }
            else
            {
                result.MethodB = FitTwoParameterModel(
                    methodBGames,
                    (d, t1, t2) => PredictScoreDeltaSigned(signedAnchorPoints, t1 - d * t2));
            }

            result.ComputedThroughYear = years.Count > 0 ? years.Max() : startYear;

            return result;
        }

        /// <summary>
        /// Computes Method B coefficients for `season` — using only games played
        /// through season - 1, never the current or later seasons, whether this runs
        /// live (naturally the case, since no future data exists yet) or as part of a
        /// backfill (explicitly capped here, so a backfilled season doesn't pull in
        /// every game up through today) — and persists a new row.
        ///
        /// Returns null and does NOT persist if MethodB.ExclusionReason is set — either
        /// zero cross-tier games in the season-1-and-earlier window, or a Curve 1 too
        /// thin for any bucket to reach MinAnchorSampleSize (which would otherwise
        /// silently collapse every prediction to 0 and make the "solved" D meaningless
        /// — the grid search's first-tested value, not a real fit). Both are expected
        /// for the first season or two of any full historical backfill (e.g. season
        /// 1965 has no prior data at all) — persisting a D=0/C=0/RMSE=0 row in either
        /// case would be indistinguishable from a real computed value rather than "no
        /// data available yet."
        ///
        /// Intended to run BEFORE InitializeSeasonAsync in RunSeasonSetupAsync, per
        /// the project's season-setup ordering. Always inserts when there IS usable
        /// data (append-only, per TierDiscountCoefficient remarks) — does not check
        /// for an existing row first; running this twice for the same season is
        /// intentional (e.g. recomputing after more games are loaded) and both rows
        /// remain, with GetLatestBySeasonAsync resolving which one is current.
        /// </summary>
        public async Task<TierDiscountCoefficient?> ComputeAndPersistCoefficientsAsync(
            int season, int startYear = 1965, CancellationToken token = default)
        {
            var result = await CalculateAsync(startYear, throughYear: season - 1, token: token);

            if (result.MethodB.GamesUsed == 0 || result.MethodB.ExclusionReason != null)
            {
                return null;
            }

            var coefficient = new TierDiscountCoefficient
            {
                Season = season,
                ComputedFromStartYear = startYear,
                ComputedThroughYear = result.ComputedThroughYear,
                WinDifferentialDiscount = (decimal)result.MethodB.SolvedDiscountCoefficient,
                CaliberGapPoints = (decimal)result.MethodB.SolvedCaliberConstant,
                TypicalPredictionErrorPoints = (decimal)result.MethodB.RmseAtSolvedParameters,
                GamesUsed = result.MethodB.GamesUsed,
                ComputedAt = DateTime.UtcNow
            };

            await _uow.TierDiscountCoefficients.AddAsync(coefficient, token);
            await _uow.SaveChangesAsync(token);

            return coefficient;
        }

        /// <summary>
        /// Runs ComputeAndPersistCoefficientsAsync for every season from startSeason
        /// through the most recent season with played FBS regular-season data (or
        /// throughSeason, if given) — for backfilling seasons that predate this
        /// feature. Mirrors RosterCapacityService.ComputeZRosterBulkAsync's pairing
        /// convention (single-season method + bulk method). Returns (Persisted, Skipped)
        /// counts — Skipped is seasons with zero games in their season-1-and-earlier
        /// window (expected for the first season or two of a full historical backfill,
        /// not an error).
        /// </summary>
        public async Task<(int Persisted, int Skipped)> ComputeAndPersistCoefficientsBulkAsync(
            int startSeason, int? throughSeason = null, int startYear = 1965, CancellationToken token = default)
        {
            int maxSeason;
            if (throughSeason.HasValue)
            {
                maxSeason = throughSeason.Value;
            }
            else
            {
                var allGames = await _uow.Games.GetPlayedGamesSinceYearAsync(startSeason, token);
                maxSeason = allGames.Count > 0 ? allGames.Max(g => g.Year) : startSeason;
            }

            var persisted = 0;
            var skipped = 0;
            for (var season = startSeason; season <= maxSeason; season++)
            {
                var result = await ComputeAndPersistCoefficientsAsync(season, startYear, token);
                if (result == null) skipped++; else persisted++;
            }

            return (persisted, skipped);
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Two-parameter fit
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Fits Predicted_g(D, C) = predictNoC(D, T1_g, T2_g) + C against real per-game
        /// margins, via bounded grid search over D with the OLS-closed-form-optimal C
        /// at each D (see class remarks — matching only the population average can't
        /// pin down two parameters, since C absorbs the mean error for any D).
        /// </summary>
        private static TwoParameterFitResult FitTwoParameterModel(
            List<(double T1, double T2, double ScoreT1, double ScoreT2)> games,
            Func<double, double, double, double> predictNoC)
        {
            var fit = new TwoParameterFitResult { GamesUsed = games.Count };

            if (games.Count == 0)
            {
                fit.ExclusionReason = "No cross-tier games available";
                return fit;
            }

            var actualAvgDelta = games.Average(g => g.ScoreT1 - g.ScoreT2);
            var undiscountedPredictedAvgDelta = games.Average(g => predictNoC(1.0, g.T1, g.T2));

            fit.ActualAvgScoreDelta = Math.Round(actualAvgDelta, 4);
            fit.UndiscountedPredictedAvgScoreDelta = Math.Round(undiscountedPredictedAvgDelta, 4);
            fit.BaselineError = Math.Round(actualAvgDelta - undiscountedPredictedAvgDelta, 4);

            var bestD = SearchMin;
            var bestC = 0.0;
            var bestSse = double.MaxValue;
            var n = games.Count;

            for (var d = SearchMin; d <= SearchMax; d += SearchStep)
            {
                double sum = 0.0, sumSq = 0.0;
                foreach (var g in games)
                {
                    var predictedNoC = predictNoC(d, g.T1, g.T2);
                    var residual = (g.ScoreT1 - g.ScoreT2) - predictedNoC;
                    sum += residual;
                    sumSq += residual * residual;
                }

                var c = sum / n;
                var sse = sumSq - (sum * sum) / n; // == Σ(residual - c)² — standard identity, avoids a second pass

                if (sse < bestSse)
                {
                    bestSse = sse;
                    bestD = d;
                    bestC = c;
                }
            }

            fit.SolvedDiscountCoefficient = Math.Round(bestD, 4);
            fit.SolvedCaliberConstant = Math.Round(bestC, 4);
            fit.RmseAtSolvedParameters = Math.Round(Math.Sqrt(bestSse / n), 4);
            fit.SseAtSolvedParameters = bestSse; // raw, unrounded — diagnostic

            foreach (var d in CheckpointValues)
            {
                double sum = 0.0, sumSq = 0.0;
                foreach (var g in games)
                {
                    var predictedNoC = predictNoC(d, g.T1, g.T2);
                    var residual = (g.ScoreT1 - g.ScoreT2) - predictedNoC;
                    sum += residual;
                    sumSq += residual * residual;
                }

                var c = sum / n;
                var sse = sumSq - (sum * sum) / n;

                fit.Checkpoints.Add(new TwoParameterCheckpoint
                {
                    D = d,
                    PredictedAvgScoreDelta = Math.Round(actualAvgDelta - sum / n, 4),
                    OptimalCaliberConstant = Math.Round(c, 4),
                    RmseAtThisD = Math.Round(Math.Sqrt(sse / n), 4),
                    SseAtThisD = sse // raw, unrounded — diagnostic
                });
            }

            return fit;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Curve 1
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds a continuous, odd-symmetric ("mirrored") version of Curve 1's anchor
        /// points.
        ///
        /// Curve 1 is built from ABSOLUTE values (|WinDiffA-WinDiffB|, |ScoreA-ScoreB|)
        /// and has no direction. An earlier approach computed a magnitude from Curve 1
        /// and reapplied Math.Sign(signedWinDiff) as a separate step — which created a
        /// real discontinuity: for two teams with the SAME NONZERO win differential (a
        /// tied record, e.g. 3-2 vs 3-2), signedWinDiff = WinDiffT1 - D*WinDiffT2 hits
        /// exactly zero ONLY at D=1 — and since that's an exactly-representable ratio
        /// for small integers, EVERY such tied-record game shares that identical
        /// threshold, producing an artificial pileup at D=1.
        ///
        /// This mirrors every anchor point (w, m) with w > 0 to (-w, -m), and forces
        /// the origin itself to (0, 0) — a win differential of exactly zero predicts a
        /// SIGNED margin of zero on average (no directional information), which
        /// necessarily discards Curve 1's own w=0 data point (a real, non-zero AVERAGE
        /// MAGNITUDE of blowout even between tied-record teams), since no continuous
        /// odd function can encode both "no directional signal" and "real nonzero
        /// magnitude" at the same point.
        /// </summary>
        private static List<TierCurvePoint> BuildSignedAnchorPoints(List<TierCurvePoint> anchorPoints)
        {
            var signed = new List<TierCurvePoint>
            {
                new() { WinDifferential = 0, AvgScoreDelta = 0.0, SampleSize = 0 }
            };

            foreach (var p in anchorPoints)
            {
                if (p.WinDifferential == 0) continue; // origin forced to (0,0) above — see remarks

                signed.Add(new TierCurvePoint
                {
                    WinDifferential = p.WinDifferential,
                    AvgScoreDelta = p.AvgScoreDelta,
                    SampleSize = p.SampleSize
                });
                signed.Add(new TierCurvePoint
                {
                    WinDifferential = -p.WinDifferential,
                    AvgScoreDelta = -p.AvgScoreDelta,
                    SampleSize = p.SampleSize
                });
            }

            return signed.OrderBy(p => p.WinDifferential).ToList();
        }

        /// <summary>
        /// Predicts a signed score delta by direct linear interpolation over a
        /// continuous, odd-symmetric signed curve (see BuildSignedAnchorPoints) — no
        /// separate magnitude+sign step, so no discontinuity anywhere, including at
        /// zero. Clamps to the farthest endpoint's value (no extrapolation) outside
        /// the observed range.
        /// </summary>
        private static double PredictScoreDeltaSigned(List<TierCurvePoint> signedAnchorPoints, double signedWinDiff)
        {
            if (signedAnchorPoints.Count == 0) return 0.0;
            if (signedAnchorPoints.Count == 1) return signedAnchorPoints[0].AvgScoreDelta;

            if (signedWinDiff <= signedAnchorPoints[0].WinDifferential) return signedAnchorPoints[0].AvgScoreDelta;
            if (signedWinDiff >= signedAnchorPoints[^1].WinDifferential) return signedAnchorPoints[^1].AvgScoreDelta;

            for (var i = 0; i < signedAnchorPoints.Count - 1; i++)
            {
                var a = signedAnchorPoints[i];
                var b = signedAnchorPoints[i + 1];

                if (signedWinDiff < a.WinDifferential || signedWinDiff > b.WinDifferential) continue;
                if (b.WinDifferential == a.WinDifferential) return a.AvgScoreDelta;

                var t = (signedWinDiff - a.WinDifferential) / (double)(b.WinDifferential - a.WinDifferential);
                return a.AvgScoreDelta + t * (b.AvgScoreDelta - a.AvgScoreDelta);
            }

            return signedAnchorPoints[^1].AvgScoreDelta; // unreachable given the range checks above
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // DTOs
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>One point on Curve 1 (Tier 1 vs Tier 1 baseline).</summary>
    public class TierCurvePoint
    {
        public int WinDifferential { get; set; }
        public double AvgScoreDelta { get; set; }
        public int SampleSize { get; set; }
    }

    public class TwoParameterFitResult
    {
        public double ActualAvgScoreDelta { get; set; }
        public double UndiscountedPredictedAvgScoreDelta { get; set; }

        /// <summary>mean(ActualAvgScoreDelta) - mean(UndiscountedPredictedAvgScoreDelta)
        /// at D=1, C=0.</summary>
        public double BaselineError { get; set; }

        /// <summary>Discount applied to Tier 2's win differential before prediction.</summary>
        public double SolvedDiscountCoefficient { get; set; }

        /// <summary>Flat additive caliber-gap term — the portion of the real margin
        /// that does NOT scale with either team's discounted input at all.</summary>
        public double SolvedCaliberConstant { get; set; }

        /// <summary>Root-mean-squared per-game error at the solved (D, C) — the actual
        /// fit-quality metric, since the mean error is ~0 by construction regardless
        /// of D once C is included.</summary>
        public double RmseAtSolvedParameters { get; set; }

        /// <summary>Raw, unrounded SSE at the solved (D, C) — diagnostic, compare
        /// directly against Checkpoints[].SseAtThisD with no Sqrt/Round in between.</summary>
        public double SseAtSolvedParameters { get; set; }
        public int GamesUsed { get; set; }
        public string? ExclusionReason { get; set; }

        /// <summary>
        /// The (C, RMSE) tradeoff at a handful of fixed D values — lets you see the
        /// actual shape of the fit surface rather than trusting the solved pair blind.
        /// </summary>
        public List<TwoParameterCheckpoint> Checkpoints { get; set; } = new();
    }

    public class TwoParameterCheckpoint
    {
        public double D { get; set; }

        /// <summary>D-only (C=0) predicted average. ActualAvgScoreDelta minus this
        /// should equal OptimalCaliberConstant at this same D, as a consistency check.</summary>
        public double PredictedAvgScoreDelta { get; set; }
        public double OptimalCaliberConstant { get; set; }
        public double RmseAtThisD { get; set; }

        /// <summary>Raw, unrounded SSE — diagnostic, compare directly against
        /// TwoParameterFitResult.SseAtSolvedParameters with no Sqrt/Round in between.</summary>
        public double SseAtThisD { get; set; }
    }

    public class TierDiscountAnalysisResult
    {
        public int StartYear { get; set; }

        /// <summary>Last year of games actually included — equal to whatever
        /// throughYear was passed to CalculateAsync, or the most recent year with
        /// played data if throughYear was null.</summary>
        public int ComputedThroughYear { get; set; }
        public TwoParameterFitResult MethodB { get; set; } = new();
        public int GamesSkippedNoPriorWeekSnapshot { get; set; }
        public int GamesSkippedNoConferenceHistory { get; set; }
    }
}
