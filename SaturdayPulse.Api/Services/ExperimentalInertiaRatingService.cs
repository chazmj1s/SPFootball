using Microsoft.Extensions.Options;
using SaturdayPulse.Configuration;
using SaturdayPulse.Contracts;
using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// PROMOTED — this IS the live rating source, despite the "EXPERIMENTAL"/
    /// "parallel...for comparison" framing this header originally had.
    /// GamePredictionService.GetRatingsForWeekAsync delegates here directly for
    /// every real prediction. Read-only: never writes to WeeklyRankings,
    /// TeamRecords, or Projections. Header corrected 2026-09-02, in the same
    /// debugging session that almost took "not wired into any live prediction
    /// path" at face value and skipped reading this file as irrelevant.
    /// RatingComparisonService still uses it for backtesting too.
    ///
    /// Mirrors GetRatingsForWeekAsync's output shape (Dictionary&lt;TeamId, TeamRecord&gt;)
    /// so RatingComparisonService can feed both rating sets through the exact same
    /// prediction math (GamePredictionService.PredictMatchupsWithRatings) without any
    /// extra mapping.
    ///
    /// Deliberately does NOT populate TeamRecord.ZRoster on its output — ZRoster is
    /// already folded into the anchor via ComputeSeededAnchorUnit before this method
    /// runs. Leaving it null means CalculatePrediction's ApplyZRosterDecay (still
    /// present, untouched, in the production code path) no-ops correctly when this
    /// service's output is later run through it — avoids double-applying ZRoster.
    ///
    /// NEW FILE — part of the K=4 inertia-blending experimental comparison path.
    ///
    /// ── UPDATED 2026-08-21 — Ranking now K=4-blended, same treatment as PowerRating ──
    ///   Previously this method computed Ranking from the FULL projected season
    ///   (actual + locked Projection rows for every future week) via
    ///   RatingCalculator.ComputeRanking — the same approach WeeklyRankingsService
    ///   uses for its own separate Ranking calculation. That's circular for any
    ///   not-yet-played week (a week's Ranking depended on later weeks' projected
    ///   outcomes) and gave a low-SOS, high-talent team (Notre Dame: #2 ZRoster,
    ///   thin schedule strength) zero protection against a bad projected season
    ///   getting baked into its Ranking before a single real game — unlike
    ///   PowerRating, which already had BlendUnit's K=4 inertia curve for exactly
    ///   this. Ranking now gets that same curve: a pure-talent anchor (derived
    ///   from anchorUnit alone, no wins/losses) blended against a real-games-only
    ///   live component, via the same _ratingBlending.BlendUnit call and
    ///   gamesPlayed weighting already used for blendedPowerRating. See the
    ///   blendedRanking computation below for the full mechanism.
    ///
    ///   AvgScoreDifferentialService and its 60-year historical table are
    ///   deliberately untouched by this — GetExpectedDistribution still keys on
    ///   Ranking the same way it always has; only Ranking's own honesty about
    ///   what's actually known at a given point in the season changed.
    /// </summary>
    public class ExperimentalInertiaRatingService
    {
        private readonly IUnitOfWork _uow;
        private readonly RatingBlendingService _ratingBlending;

        public ExperimentalInertiaRatingService(
            IUnitOfWork uow,
            RatingBlendingService ratingBlending)
        {
            _uow = uow;
            _ratingBlending = ratingBlending;
        }

        /// <summary>Per-team anchor/blend breakdown for AnalyzeAnchorAsync's diagnostic output.</summary>
        public record TeamAnchorDetail(
            int TeamId, decimal? SeedRating, decimal? ZRoster,
            double AnchorUnit, double AnchorPowerRating, decimal AnchorRanking,
            int GamesPlayed, decimal LiveRanking, decimal BlendedRanking);

        /// <summary>
        /// DIAGNOSTIC — READ-ONLY. Exposes the anchor/blend breakdown
        /// GetBlendedRatingsForWeekAsync computes internally but doesn't return, for
        /// exactly the two requested teams. Calls GetBlendedRatingsForWeekAsync
        /// itself for the final BlendedRanking (guaranteed to match what production
        /// predictions actually use), then recomputes the intermediate steps
        /// (SeedRating -> anchorUnit -> anchorPowerRating -> anchorRanking) using
        /// the exact same shared methods that call already uses (ComputeSeededAnchorUnit,
        /// BuildLeagueYearStats, RatingScaling.FromUnitScale) — not a second guess
        /// at the math, just the same steps made visible instead of collapsed.
        ///
        /// Added 2026-09-03 to check whether GamePredictionService's projected
        /// margins for Texas's 2026 schedule made sense given SeedRating's real
        /// dispersion — liveMean/liveStdDev come from the FULL FBS week's PowerRating
        /// distribution, not a hand-picked subset, which is exactly the piece a
        /// manual spot-check can't safely approximate.
        /// </summary>
        public async Task<(TeamAnchorDetail Team, TeamAnchorDetail Opponent)> AnalyzeAnchorAsync(
            int teamId, int opponentId, int year, int week, CancellationToken token = default)
        {
            var blended = await GetBlendedRatingsForWeekAsync(year, week, token);

            var currentYearTeamRecords = await _uow.TeamRecords.GetByYearAsync(year, token);
            var teamsDict = await _uow.Teams.GetByTeamIdsAsync(
                currentYearTeamRecords.Select(r => r.TeamID).ToList(), token);
            var leagueStats = RollingAverageService.BuildLeagueYearStats(currentYearTeamRecords, teamsDict);
            leagueStats.TryGetValue((short)year, out var yearStats);
            double liveMean = yearStats.Mean;
            double liveStdDev = yearStats.StdDev;

            // Anchor conversion needs a stable, non-circular distribution — the
            // current year's own PowerRating column is either freshly seeded or
            // was itself written by a prior pass of this same FromUnitScale call,
            // so its stddev converges toward whatever the last pass produced
            // rather than reflecting real dispersion. SeedRating's z-score was
            // built off finalized historical years (RollingAverageService.
            // ComputeSeed), so the reverse mapping needs a matching finalized-
            // year distribution, not the in-progress one. Falls back to
            // liveMean/liveStdDev if year-1 has no data (earliest year in a
            // from-scratch backfill) — same "missing = safe default" convention
            // already used for anchorBlendCoefficient below.
            var priorYearRecords = await _uow.TeamRecords.GetHistoricalAsync(
                fromYear: year - 1, toYearExclusive: year, token);
            var anchorStats = RollingAverageService.BuildLeagueYearStats(priorYearRecords, teamsDict);
            anchorStats.TryGetValue((short)(year - 1), out var anchorYearStats);
            double anchorMean = anchorYearStats.StdDev > 0 ? anchorYearStats.Mean : liveMean;
            double anchorStdDev = anchorYearStats.StdDev > 0 ? anchorYearStats.StdDev : liveStdDev;

            var lastPlayedWeekByTeam = await _uow.Games.GetLastPlayedWeekByTeamAsync(year, token);
            int capWeek = Math.Max(week - 1, 0);
            var anchorBlendCoefficient = await _uow.AnchorBlendCoefficients.GetLatestBySeasonAsync(year, token);

            // Resolve each team's live week first, then fetch both snapshots with
            // real awaits — avoids blocking on async work inside the local
            // function below (GetAwaiter().GetResult() risks a deadlock under a
            // sync context; not worth it just to keep Detail() non-async).
            int teamLiveWeek = Math.Min(
                lastPlayedWeekByTeam.TryGetValue(teamId, out var tw) ? tw : 0, capWeek);
            int oppLiveWeek = Math.Min(
                lastPlayedWeekByTeam.TryGetValue(opponentId, out var ow) ? ow : 0, capWeek);

            var liveByTeam = new Dictionary<int, WeeklyRanking>();
            foreach (var w in new[] { teamLiveWeek, oppLiveWeek }.Distinct())
            {
                var snapshot = await _uow.WeeklyRankings.GetByYearAndWeekAsync(year, w, token);
                foreach (var wr in snapshot)
                    if (wr.TeamID == teamId || wr.TeamID == opponentId)
                        liveByTeam[wr.TeamID] = wr;
            }

            TeamAnchorDetail Detail(int id)
            {
                var teamRecord = currentYearTeamRecords.FirstOrDefault(r => r.TeamID == id);
                double anchorUnit = teamRecord != null
                    ? _ratingBlending.ComputeSeededAnchorUnit(teamRecord, anchorBlendCoefficient)
                    : 0.5;
                double anchorPowerRating = RatingScaling.FromUnitScale(anchorUnit, anchorMean, anchorStdDev);
                decimal anchorRanking = 0.5m * (1m + (decimal)anchorPowerRating);

                liveByTeam.TryGetValue(id, out var live);
                int gamesPlayed = live != null ? live.Wins + live.Losses : 0;

                decimal liveRanking = live != null
                    ? RatingCalculator.ComputeRanking(live.Wins, live.Losses, live.PowerRating ?? 0m)
                        ?? anchorRanking
                    : anchorRanking;

                blended.TryGetValue(id, out var blendedRecord);
                decimal blendedRanking = blendedRecord?.Ranking ?? anchorRanking;

                return new TeamAnchorDetail(
                    id, teamRecord?.SeedRating, teamRecord?.ZRoster,
                    anchorUnit, anchorPowerRating, anchorRanking,
                    gamesPlayed, liveRanking, blendedRanking);
            }

            return (Detail(teamId), Detail(opponentId));
        }

        /// <summary>
        /// K=4 inertia-blended ratings for a given year/week. No hard cliff at any week —
        /// every team's rating is anchor+live blended by gamesPlayed weight.
        /// </summary>
        public async Task<Dictionary<int, TeamRecord>> GetBlendedRatingsForWeekAsync(
            int year, int week, CancellationToken token = default)
        {
            // No week-0 (or prior-year) snapshot fetch here, deliberately. The anchor
            // comes entirely from TeamRecord.TrendRating / TeamRecord.ZRoster via
            // ComputeSeededAnchorUnit below — both persistent, week-independent
            // columns, not a materialized WeeklyRankings row. Confirmed with Charlie
            // (2026 preseason ratings are never displayed and are bogus anyway pre-
            // roster-data-load) that week 0 has no remaining consumer in this pipeline:
            // SOS's fallback already points at SeedRating independently, and there's
            // no display path reading a week-0 row. An earlier version of this method
            // fetched this snapshot and never used the result — pure dead weight, gone
            // now. NOT the source of the week-1 gamesPlayed inflation flagged in the
            // last comparison run (13-16 instead of 0) — that comes from the live
            // snapshot fetch just below, at liveWeek=0, which is a separate, still-open
            // question: whether WeeklyRankings' own week-0 rows are seeded with
            // prior-year Wins/Losses. Worth checking WeeklyRankingsService's season-init
            // path directly before assuming; don't want to misattribute a second time.

            // ── LIVE SOURCE — pinned to last ACTUALLY PLAYED week, not week-1 chained ──
            //
            // FIXED 2026-08-20 (Notre Dame diagnosis). Previously: liveWeek =
            // Math.Max(week - 1, 0), walking back exactly one week regardless of
            // whether that week's WeeklyRankings row reflected real games or was
            // itself a not-yet-played projection generated the same way, one step
            // earlier in the same chain. In a preseason backfill (or any team on
            // a bad early run before real games catch up), this let a team's own
            // projected losses compound: week N's bad projected score lowered
            // week N's blended rating, which became week N+1's "live" anchor,
            // producing an even worse projection, repeating until PredictedSpread
            // grew large enough to round a team's score below zero (see
            // GamePredictionService.BuildProjection's companion fix).
            //
            // Every not-yet-played week now anchors to the SAME last-real-week
            // snapshot — no week-to-week chaining through unverified projected
            // data. For a virgin season (nothing played yet), that's week 0 for
            // every future week. Once real games start, ALL remaining unplayed
            // weeks move together to the new last-played week as soon as it's
            // available — not incrementally, one week at a time.
            //
            // Math.Min guard: never anchor to a week >= the one being projected,
            // even if playedWeeks somehow includes a week at or beyond `week`
            // (e.g. a historical backfill/regeneration call) — preserves
            // causality regardless of caller context.
            //
            // NOTE: GetPlayedWeeksByYearAsync returns List<PlayedWeekDto>, not
            // List<int> — .Week assumed as the property name (matches every
            // other week-bearing type in this codebase: WeeklyRanking.Week,
            // GetByYearAndWeekAsync(year, week), etc.). Confirm against the
            // real PlayedWeekDto definition in ProductionDTOs.cs if this
            // doesn't compile — that's the only remaining guess here.
            //
            // FIXED 2026-08-21 (ND bye/late-schedule-gap diagnosis). The prior
            // version of this block called GetPlayedWeeksByYearAsync (LEAGUEWIDE —
            // no TeamId filter, returns any week where ANY game that year had two
            // non-zero scores) and took a single global Max(Week) applied to every
            // team. That's wrong whenever the schedule isn't uniform across teams:
            // week 15 currently holds only Army-Navy — itself a regular-season
            // game, NOT conference championship week (corrected from an earlier
            // wrong assumption made mid-session) — so it enters the leaguewide
            // list on the strength of that one game alone, but a team not in that
            // game (e.g. Notre Dame) has no real game that week. The old code
            // still resolved liveWeek=15 for every other team and pulled its
            // week-15 WeeklyRankings row, which isn't backed by any real game for
            // them, instead of holding at their actual last-played week.
            // Confirmed via WeeklyRanking data: ND's PowerRating sat stable through
            // week 13 (-0.0023), then reverted to -0.3533 at week 15 — nearly the
            // week-1 starting value — once this bug's leaguewide max pulled in a
            // week-15 snapshot with no real ND game behind it.
            //
            // Now resolved per team via GetLastPlayedWeekByTeamAsync, still capped
            // by the same Math.Min causality guard (never anchor to a week >= the
            // one being projected) on a per-team basis.
            var currentYearTeamRecords = await _uow.TeamRecords.GetByYearAsync(year, token);
            var teamsDict = await _uow.Teams.GetByTeamIdsAsync(
                currentYearTeamRecords.Select(r => r.TeamID).ToList(), token);

            var lastPlayedWeekByTeam = await _uow.Games.GetLastPlayedWeekByTeamAsync(year, token);
            int capWeek = Math.Max(week - 1, 0);

            var teamLiveWeeks = currentYearTeamRecords.ToDictionary(
                r => r.TeamID,
                r => Math.Min(
                    lastPlayedWeekByTeam.TryGetValue(r.TeamID, out var w) ? w : 0,
                    capWeek));

            // Batched by distinct resolved week rather than one GetByYearAndWeekAsync
            // call per team — most teams will share the same resolved week (the
            // current global "everyone who's on schedule" week), with only the
            // handful of teams on a bye or without that week's game type (e.g.
            // Independents during championship week) resolving to something earlier.
            var liveByTeam = new Dictionary<int, WeeklyRanking>();
            foreach (var w in teamLiveWeeks.Values.Distinct())
            {
                var snapshot = await _uow.WeeklyRankings.GetByYearAndWeekAsync(year, w, token);
                foreach (var wr in snapshot)
                {
                    if (teamLiveWeeks.TryGetValue(wr.TeamID, out var resolvedWeek) && resolvedWeek == w)
                        liveByTeam[wr.TeamID] = wr;
                }
            }

            // Latest calibrated ZRoster/SeedRating blend weights for this season —
            // see RatingBlendingService.ComputeSeededAnchorUnit remarks (2026-08-20)
            // for why this replaced the old hardcoded 50/50 + zRosterScalingConstant.
            // Null is expected and handled gracefully (ComputeSeededAnchorUnit falls
            // back to the prior default weighting) if AnchorBlendCalculator hasn't
            // run yet for this season — same "missing coefficient = safe default"
            // convention as TierDiscountCoefficient's null-handling in BuildProjection.
            var anchorBlendCoefficient = await _uow.AnchorBlendCoefficients.GetLatestBySeasonAsync(year, token);

            // Cross-sectional PowerRating distribution used to z-score the live
            // component onto the same [0,1] scale as SeedRating/anchor before
            // blending. Deliberately sourced from currentYearTeamRecords (full FBS
            // coverage, just fetched above) rather than liveSnapshot — liveSnapshot
            // at early weeks (e.g. before any real games, liveWeek=0) has thin,
            // possibly incomplete coverage, and a small/unrepresentative sample here
            // badly distorts
            // FromUnitScale's inverse mapping for EVERY team that week, not just
            // ones with missing data themselves. This was the actual cause of the
            // week 2-5 volatility (LSU vs Louisiana Tech: 31.7-point total swing,
            // no FCS involved) — the FCS placeholder fix was correct and necessary
            // but was masking this as a second, separate bug underneath it. This is
            // "Fix 1" from earlier in the session — flagged then, never actually
            // implemented until now.
            //
            // Reuses RollingAverageService.BuildLeagueYearStats (already FBS-filtered,
            // already handles the mean/stddev math) rather than re-deriving the same
            // calculation a third time in this codebase.
            var leagueStats = RollingAverageService.BuildLeagueYearStats(currentYearTeamRecords, teamsDict);
            leagueStats.TryGetValue((short)year, out var yearStats);
            double liveMean = yearStats.Mean;
            double liveStdDev = yearStats.StdDev;
            // Degenerate case (liveStdDev == 0, e.g. very start of a brand-new
            // season before any PowerRating exists yet for `year`): RatingScaling.
            // FromUnitScale already handles stdDev<=0 by returning `mean` for
            // every team — safe, if uninformative, rather than a crash or NaN.

            // Anchor conversion needs a stable, non-circular distribution — see
            // AnalyzeAnchorAsync's identical block above for the full rationale.
            // currentYearTeamRecords.PowerRating (liveMean/liveStdDev's source)
            // is either freshly seeded or was itself written by a prior pass of
            // this same FromUnitScale call, so it converges toward whatever the
            // last pass produced rather than reflecting SeedRating's real,
            // finalized-year dispersion. Falls back to liveMean/liveStdDev if
            // year-1 has no data (earliest year in a from-scratch backfill).
            var priorYearRecords = await _uow.TeamRecords.GetHistoricalAsync(
                fromYear: year - 1, toYearExclusive: year, token);
            var anchorStats = RollingAverageService.BuildLeagueYearStats(priorYearRecords, teamsDict);
            anchorStats.TryGetValue((short)(year - 1), out var anchorYearStats);
            double anchorMean = anchorYearStats.StdDev > 0 ? anchorYearStats.Mean : liveMean;
            double anchorStdDev = anchorYearStats.StdDev > 0 ? anchorYearStats.StdDev : liveStdDev;

            // FULL-SEASON (actual + projected) Win/Loss rollup REMOVED 2026-08-21.
            // Previously fed ComputeRanking's wins/losses argument with the whole
            // locked schedule (actual results + Projection rows for every
            // not-yet-played week) — see git history for the removed
            // ResolvedGameResults query and per-team tally loop that used to sit
            // here. That made Ranking circular for any not-yet-played week: week
            // 1's Ranking depended on weeks 2-15's PROJECTED outcomes, which
            // themselves depend on ratings downstream of week 1's own Ranking.
            // Worse, it meant a team with a genuinely elite roster but a light/
            // untested schedule (Notre Dame: #2 ZRoster, low SOS) got a full
            // projected-loss season baked into Ranking before a single real game
            // was played, with no protection at all — unlike PowerRating, which
            // already had BlendUnit's K=4 inertia curve keeping it talent-led
            // until real results accumulate.
            //
            // Ranking now gets that exact same treatment — see the blendedRanking
            // computation in the loop below, which reuses _ratingBlending.
            // BlendUnit with the same gamesPlayed weighting already computed for
            // blendedPowerRating. AvgScoreDifferentialService and its 60-year
            // table are untouched — GetExpectedDistribution still keys on Ranking
            // the same way it always has; this only changes how honestly Ranking
            // reflects what's actually known about a team at a given point in the
            // season, rather than laundering the full projected schedule through it.

            var result = new Dictionary<int, TeamRecord>();

            foreach (var teamRecord in currentYearTeamRecords)
            {
                // FCS teams already have an entry in currentYearTeamRecords —
                // RollingAverageService includes them, just with SeedRating/
                // TrendRating/PedigreeRating forced to 0 (a literal 0, not null).
                // Left alone, ComputeSeededAnchorUnit would read that as seedUnit=0.0
                // — the worst possible team on the [0,1] scale, not a deliberate
                // placeholder. This was the source of the Clemson/Kentucky/Auburn-vs-
                // FCS-opponent deltas still showing up in the "converged" weeks 6-14.
                // Skip the anchor/blend math entirely for FCS and use the exact same
                // fixed placeholder production's GetRatingsForWeekAsync uses, so both
                // methods treat FCS opponents identically and any remaining delta on
                // those games reflects the FBS team's own rating, not two different
                // guesses about the FCS side.
                if (teamsDict.TryGetValue(teamRecord.TeamID, out var team) &&
                    string.Equals(team.Division, "fcs", StringComparison.OrdinalIgnoreCase))
                {
                    result[teamRecord.TeamID] = new TeamRecord
                    {
                        TeamID           = teamRecord.TeamID,
                        Year             = (short)year,
                        Ranking          = 0.03m,
                        PowerRating      = -0.50m,
                        Wins             = 0,
                        Losses           = 0,
                        PointsFor        = 280,
                        PointsAgainst    = 420,
                        AvgPointsScored  = 20m,
                        AvgPointsAllowed = 30m
                    };
                    continue;
                }

                double anchorUnit = _ratingBlending.ComputeSeededAnchorUnit(
                    teamRecord, anchorBlendCoefficient);

                liveByTeam.TryGetValue(teamRecord.TeamID, out var live);
                int gamesPlayed = live != null ? live.Wins + live.Losses : 0;

                double liveUnit = live?.PowerRating.HasValue == true
                    ? RatingScaling.ToUnitScale((double)live.PowerRating.Value, liveMean, liveStdDev)
                    : anchorUnit; // no live data yet; gamesPlayed=0 makes this moot anyway

                double blendedUnit = _ratingBlending.BlendUnit(anchorUnit, liveUnit, gamesPlayed);
                double blendedPowerRating = RatingScaling.FromUnitScale(blendedUnit, liveMean, liveStdDev);

                // Ranking — K=4 BLENDED 2026-08-21, same treatment as
                // blendedPowerRating above, same _ratingBlending.BlendUnit call,
                // same gamesPlayed weighting. Two components, computed on the
                // Ranking scale (0.5 * (1 + powerRating) — RatingCalculator.
                // ComputeRanking's own formula, and the same fallback already
                // used elsewhere in this codebase — e.g. RatingCalculator.
                // ResolveStrength's PowerRating-derived tier) rather than blended
                // on the unit scale and converted once:
                //
                //   anchorRanking — pure roster/talent, no wins/losses at all.
                //   Derived from anchorUnit alone (the same anchorUnit already
                //   computed above for blendedPowerRating), converted to a raw
                //   PowerRating via FromUnitScale, then to the Ranking scale.
                //   This is what "we can't project future performance from a
                //   schedule that hasn't happened yet" actually means in code —
                //   SOS and an unplayed schedule contribute nothing here.
                //
                //   liveRanking — ComputeRanking from ONLY real, actually-played
                //   games (live.Wins/live.Losses from liveByTeam — per-team real-
                //   games-only as of today's bye/championship-week fix, no
                //   projected games mixed in), using this team's own
                //   blendedPowerRating. Null (no real games yet) falls back to
                //   anchorRanking — matches gamesPlayed=0 making the blend below
                //   moot anyway.
                //
                // Blended with the exact same K=4 curve as PowerRating: pure
                // talent at gamesPlayed=0, live record dominates once real games
                // accumulate (≈60% live by week 6, matching PowerRating's curve).
                double anchorPowerRating = RatingScaling.FromUnitScale(anchorUnit, anchorMean, anchorStdDev);
                decimal anchorRanking = 0.5m * (1m + (decimal)anchorPowerRating);

                decimal liveRanking = live != null
                    ? RatingCalculator.ComputeRanking(live.Wins, live.Losses, (decimal)blendedPowerRating)
                        ?? anchorRanking
                    : anchorRanking;

                decimal blendedRanking = (decimal)_ratingBlending.BlendUnit(
                    (double)anchorRanking, (double)liveRanking, gamesPlayed);

                result[teamRecord.TeamID] = new TeamRecord
                {
                    TeamID = teamRecord.TeamID,
                    Year = (short)year,
                    Wins = live?.Wins ?? 0,
                    Losses = live?.Losses ?? 0,
                    PointsFor = live?.PointsFor ?? 0,
                    PointsAgainst = live?.PointsAgainst ?? 0,
                    PowerRating = (decimal)Math.Round(blendedPowerRating, 4),
                    Ranking = blendedRanking,
                    CombinedSOS = live?.CombinedSOS,
                    BaseSOS = live?.BaseSOS,
                    SubSOS = live?.SubSOS,
                    AvgPointsScored = live?.AvgPointsScored ?? teamRecord.AvgPointsScored,
                    AvgPointsAllowed = live?.AvgPointsAllowed ?? teamRecord.AvgPointsAllowed
                };
            }

            return result;
        }
    }
}
