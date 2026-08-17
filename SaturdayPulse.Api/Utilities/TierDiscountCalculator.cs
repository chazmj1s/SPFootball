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
    ///     its purpose: it measured this same model against the REAL, live
    ///     IAvgScoreDifferentialService output instead of real win-loss records, and
    ///     confirmed Method B's D landed close to Production's. Deliberately NOT
    ///     revisited when this model was later rebuilt (below) — Production's fit
    ///     target (AvgScoreDifferentialService's Ranking-based margin) already has
    ///     tier-blindness baked into it via Ranking, which is the exact bias this
    ///     calculator exists to correct for. Fitting against it would launder that
    ///     bias back in rather than removing it. Method B's real-win-loss-record
    ///     target is deliberately kept as the only input.
    ///   - Method B (MOV Variance Test) — the one that's actually applied. See remarks
    ///     below for the current method.
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
    /// ── Method B — MOV Variance Test ─────────────────────────────────────────────────
    /// TIER-oriented (Tier1Team - Tier2Team), not record-oriented — discounts the
    /// TIER 2 team's win differential input specifically, regardless of which team has
    /// the better raw record in a given matchup.
    ///
    /// Fits Predicted_g(D, K, C) = K * (WinDiffT1 - D * WinDiffT2) + C directly against
    /// real per-game score margins (ScoreT1 - ScoreT2) — three parameters, all solved
    /// from real cross-tier games, with no intermediate curve or lookup table:
    ///   D — discount applied to Tier 2's win differential before comparison.
    ///   K — points per unit of discounted win-differential; what actually converts
    ///       the discounted record gap into points.
    ///   C — flat additive caliber-gap constant; the portion of the real margin that
    ///       does NOT scale with either team's record at all.
    ///
    /// (Earlier version of this model converted the discounted win-differential to
    /// points via linear interpolation over a Tier1-vs-Tier1-only "Curve 1" — see git
    /// history for BuildSignedAnchorPoints/PredictScoreDeltaSigned/TierCurvePoint if
    /// that's ever needed again. That curve was never persisted anywhere, which meant
    /// D had no usable meaning downstream of this calculator — BuildProjection had a
    /// discount factor with nothing to apply it to. Replaced with the direct 3-parameter
    /// linear fit below specifically so D, K, and C are all plain persisted numbers a
    /// downstream consumer can use with nothing but arithmetic.)
    ///
    /// For a fixed D, the OLS-optimal (K, C) has a closed form — the standard simple
    /// linear regression of ScoreDelta against the discounted win-differential — which
    /// collapses the three-parameter problem to a 1D grid search over D alone,
    /// minimizing SSE (sum of squared per-game errors) at each D's optimal (K, C).
    ///
    /// RmseAtNoDiscount (D=1, i.e. Tier 2's record taken at face value, K/C still
    /// OLS-fit) vs RmseAtSolvedParameters is the real methodology check here — how much
    /// actually discounting Tier 2's record improves the fit, versus not discounting it
    /// at all.
    /// </summary>
    public class TierDiscountCalculator(IUnitOfWork _uow)
    {
        // Bounded grid search range/step over D — real, potentially noisy game data, so
        // a closed-form or bisection solve for D isn't assumed safe. K and C are always
        // solved in closed form at each D (see FitTierDiscountModel).
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

            // ── Shared per-game pass: build the cross-tier game list. ──────────────
            // (Previously also built Curve 1 — a Tier1-vs-Tier1-only baseline used to
            // convert win-differential to points via interpolation. Method B no longer
            // needs it; see class remarks. Tier1-vs-Tier1 games are simply skipped now,
            // same as Tier2-vs-Tier2 always was.)
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

                if (homeTier1 != awayTier1)
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
                // both true (Tier1 vs Tier1) or both false (Tier2 vs Tier2) — not
                // relevant to this measurement.
            }

            result.GamesSkippedNoConferenceHistory = skippedNoConferenceHistory;
            result.GamesSkippedNoPriorWeekSnapshot = skippedNoPriorWeekSnapshot;

            result.MethodB = FitTierDiscountModel(methodBGames);

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
        /// zero cross-tier games in the season-1-and-earlier window, or (far less
        /// likely) every candidate D in the grid search producing a degenerate,
        /// zero-variance fit (see FitTierDiscountModel remarks). The zero-games case is
        /// expected for the first season or two of any full historical backfill (e.g.
        /// season 1965 has no prior data at all) — persisting a D=0/K=0/C=0/RMSE=0 row
        /// in that case would be indistinguishable from a real computed value rather
        /// than "no data available yet."
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
                PointsPerWinDifferential = (decimal)result.MethodB.SolvedPointsPerWinDifferential,
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
        // Tier discount fit
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Fits Predicted_g(D, K, C) = K * (T1_g - D * T2_g) + C directly against real
        /// per-game margins — bounded grid search over D, with the OLS-closed-form-
        /// optimal (K, C) via simple linear regression at each D (see class remarks —
        /// matching only the population average can't pin down K and C independently
        /// of D, since a regression's C always absorbs the mean error regardless of D;
        /// K and C together are what's actually solved for at each candidate D).
        /// </summary>
        private static TwoParameterFitResult FitTierDiscountModel(
            List<(double T1, double T2, double ScoreT1, double ScoreT2)> games)
        {
            var fit = new TwoParameterFitResult { GamesUsed = games.Count };

            if (games.Count == 0)
            {
                fit.ExclusionReason = "No cross-tier games available";
                return fit;
            }

            var n = games.Count;
            var actualAvgDelta = games.Average(g => g.ScoreT1 - g.ScoreT2);
            fit.ActualAvgScoreDelta = Math.Round(actualAvgDelta, 4);

            // Always trivially equal under this model — see field remarks on
            // TwoParameterFitResult for why these two no longer carry information.
            fit.UndiscountedPredictedAvgScoreDelta = fit.ActualAvgScoreDelta;
            fit.BaselineError = 0.0;

            // OLS regression of ScoreDelta against the discounted win-differential
            // (T1 - D*T2), closed form via the standard sum identities — avoids a
            // second pass through games at each D. Returns null if Sxx is ~0 (every
            // game's discounted win-differential collapsed to the same value at this
            // D — degenerate, no meaningful slope), in which case this D is skipped
            // by the caller rather than reporting a division-by-zero fit.
            (double K, double C, double Sse)? FitAtD(double d)
            {
                double sumX = 0, sumY = 0, sumXX = 0, sumXY = 0, sumYY = 0;
                foreach (var g in games)
                {
                    var x = g.T1 - d * g.T2;
                    var y = g.ScoreT1 - g.ScoreT2;
                    sumX += x; sumY += y; sumXX += x * x; sumXY += x * y; sumYY += y * y;
                }

                var xbar = sumX / n;
                var ybar = sumY / n;
                var sxx = sumXX - n * xbar * xbar;
                var sxy = sumXY - n * xbar * ybar;
                var syy = sumYY - n * ybar * ybar;

                if (sxx < 1e-9) return null; // degenerate at this D — see remarks above

                var k = sxy / sxx;
                var c = ybar - k * xbar;
                var sse = syy - k * sxy; // standard identity: SSE = Syy - K*Sxy

                return (k, c, sse);
            }

            // RmseAtNoDiscount: D=1 (Tier 2's record taken at face value, K/C still
            // OLS-fit) — the real methodology check, not the old curve-based
            // "predict with C=0" baseline (see class remarks — that comparison had no
            // clean equivalent once K itself has to be fit rather than looked up).
            var atNoDiscount = FitAtD(1.0);
            fit.RmseAtNoDiscount = atNoDiscount.HasValue
                ? Math.Round(Math.Sqrt(atNoDiscount.Value.Sse / n), 4)
                : (double?)null;

            var bestD = SearchMin;
            var bestK = 0.0;
            var bestC = 0.0;
            var bestSse = double.MaxValue;

            for (var d = SearchMin; d <= SearchMax; d += SearchStep)
            {
                var result = FitAtD(d);
                if (result == null) continue;

                if (result.Value.Sse < bestSse)
                {
                    bestSse = result.Value.Sse;
                    bestD = d;
                    bestK = result.Value.K;
                    bestC = result.Value.C;
                }
            }

            if (bestSse == double.MaxValue)
            {
                // Every D in the search range was degenerate (Sxx ~0) — would need a
                // genuinely pathological dataset (e.g. every game's T2 = T1, so T1-D*T2
                // is constant across every D). Not expected in practice; surfaced
                // explicitly rather than persisting a meaningless D=SearchMin/K=0/C=0 row.
                fit.ExclusionReason = "No candidate D produced a non-degenerate fit (every discounted win-differential was constant across the game population)";
                return fit;
            }

            fit.SolvedDiscountCoefficient = Math.Round(bestD, 4);
            fit.SolvedPointsPerWinDifferential = Math.Round(bestK, 4);
            fit.SolvedCaliberConstant = Math.Round(bestC, 4);
            fit.RmseAtSolvedParameters = Math.Round(Math.Sqrt(bestSse / n), 4);
            fit.SseAtSolvedParameters = bestSse; // raw, unrounded — diagnostic

            foreach (var d in CheckpointValues)
            {
                var result = FitAtD(d);
                if (result == null) continue; // degenerate at this checkpoint — omitted rather than faked

                fit.Checkpoints.Add(new TwoParameterCheckpoint
                {
                    D = d,
                    OptimalPointsPerWinDifferential = Math.Round(result.Value.K, 4),
                    OptimalCaliberConstant = Math.Round(result.Value.C, 4),
                    RmseAtThisD = Math.Round(Math.Sqrt(result.Value.Sse / n), 4),
                    SseAtThisD = result.Value.Sse // raw, unrounded — diagnostic
                });
            }

            return fit;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // DTOs
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// One point on the old Curve 1 (Tier 1 vs Tier 1 baseline) interpolation table.
    /// No longer built or consumed anywhere in this file as of the 3-parameter linear
    /// rewrite (see class remarks) — left defined only because it's public and this
    /// codebase hasn't confirmed there's no other consumer. If nothing else in the
    /// solution references this (check Find All References), safe to delete.
    /// </summary>
    public class TierCurvePoint
    {
        public int WinDifferential { get; set; }
        public double AvgScoreDelta { get; set; }
        public int SampleSize { get; set; }
    }

    public class TwoParameterFitResult
    {
        public double ActualAvgScoreDelta { get; set; }

        /// <summary>
        /// No longer meaningful as a distinct diagnostic under the 3-parameter linear
        /// fit — by OLS construction, the fitted mean always exactly equals the actual
        /// mean at any D once (K, C) are jointly solved, so this is always equal to
        /// ActualAvgScoreDelta and BaselineError is always 0. Left in place rather than
        /// removed since the old curve-based model used these fields for a genuine
        /// "how big is the caliber gap before any correction" check that doesn't
        /// translate to this model. See RmseAtNoDiscount for the model's actual
        /// equivalent methodology check (does discounting help at all, vs D=1).
        /// </summary>
        public double UndiscountedPredictedAvgScoreDelta { get; set; }

        /// <summary>Always 0 under this model — see UndiscountedPredictedAvgScoreDelta remarks.</summary>
        public double BaselineError { get; set; }

        /// <summary>
        /// RMSE at D=1 (Tier 2's win differential taken at face value — no discount —
        /// with K and C still OLS-fit). Compare against RmseAtSolvedParameters: the gap
        /// between the two is the actual answer to "does discounting Tier 2's record
        /// help the fit at all." Null only in the degenerate case where D=1 itself
        /// produced a zero-variance discounted win-differential across every game
        /// (see FitTierDiscountModel remarks) — not expected in practice.
        /// </summary>
        public double? RmseAtNoDiscount { get; set; }

        /// <summary>Discount applied to Tier 2's win differential before prediction.</summary>
        public double SolvedDiscountCoefficient { get; set; }

        /// <summary>
        /// Points per unit of discounted win-differential (WinDiffT1 - D*WinDiffT2) —
        /// what actually converts the discounted record gap into points. Solved
        /// jointly with SolvedCaliberConstant via closed-form linear regression at the
        /// solved D.
        /// </summary>
        public double SolvedPointsPerWinDifferential { get; set; }

        /// <summary>Flat additive caliber-gap term — the portion of the real margin
        /// that does NOT scale with either team's discounted input at all.</summary>
        public double SolvedCaliberConstant { get; set; }

        /// <summary>Root-mean-squared per-game error at the solved (D, K, C) — the
        /// actual fit-quality metric, since the mean error is ~0 by construction
        /// regardless of D once K and C are jointly fit.</summary>
        public double RmseAtSolvedParameters { get; set; }

        /// <summary>Raw, unrounded SSE at the solved (D, K, C) — diagnostic, compare
        /// directly against Checkpoints[].SseAtThisD with no Sqrt/Round in between.</summary>
        public double SseAtSolvedParameters { get; set; }
        public int GamesUsed { get; set; }
        public string? ExclusionReason { get; set; }

        /// <summary>
        /// The (K, C, RMSE) tradeoff at a handful of fixed D values — lets you see the
        /// actual shape of the fit surface rather than trusting the solved triple blind.
        /// A given D is simply omitted here if it produced a degenerate (zero-variance)
        /// fit — see FitTierDiscountModel remarks.
        /// </summary>
        public List<TwoParameterCheckpoint> Checkpoints { get; set; } = new();
    }

    public class TwoParameterCheckpoint
    {
        public double D { get; set; }
        public double OptimalPointsPerWinDifferential { get; set; }
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
