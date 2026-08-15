using Microsoft.Extensions.Options;
using SaturdayPulse.Configuration;
using SaturdayPulse.Contracts;
using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// EXPERIMENTAL — parallel rating path for comparison against the production
    /// snapshot-cliff method in GamePredictionService.GetRatingsForWeekAsync.
    /// Read-only: never writes to WeeklyRankings, TeamRecords, or Projections.
    /// Not wired into any live prediction path. Delete or promote after backtesting.
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
    /// </summary>
    public class ExperimentalInertiaRatingService
    {
        private readonly IUnitOfWork _uow;
        private readonly RatingBlendingService _ratingBlending;

        // Same placeholder value as GamePredictionService.ZRosterScalingConstant —
        // kept independent here deliberately, so tuning one doesn't silently move
        // the other while these two paths are still being compared.
        private const decimal ZRosterScalingConstant = 0.05m;

        public ExperimentalInertiaRatingService(
            IUnitOfWork uow,
            RatingBlendingService ratingBlending)
        {
            _uow = uow;
            _ratingBlending = ratingBlending;
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

            // Live source: most recent available snapshot up to week-1. This method
            // previously accepted a `useWeekAsLive` flag so a caller with already-
            // persisted data for `week` itself (a just-written WeeklyRankings
            // snapshot from this week's ComputeAndSaveAsync run, or a historical
            // backfill snapshot) could use that week directly instead of stepping
            // back one. Removed — confirmed via solution-wide Find All References
            // that no caller anywhere passed true once DeveloperService.
            // BackfillProjectionsStreamAsync was deleted (its two originating
            // callers, that method and ComputeAndSaveAsync's old step 17, are both
            // gone now — see GamePredictionService.PredictMatchups remarks for the
            // full history). Always steps back one week.
            int liveWeek = Math.Max(week - 1, 0);
            var liveSnapshot = await _uow.WeeklyRankings.GetByYearAndWeekAsync(year, liveWeek, token);
            var liveByTeam = liveSnapshot.ToDictionary(wr => wr.TeamID);

            var currentYearTeamRecords = await _uow.TeamRecords.GetByYearAsync(year, token);
            var teamsDict = await _uow.Teams.GetByTeamIdsAsync(
                currentYearTeamRecords.Select(r => r.TeamID).ToList(), token);

            // Cross-sectional PowerRating distribution used to z-score the live
            // component onto the same [0,1] scale as TrendRating/anchor before
            // blending. Deliberately sourced from currentYearTeamRecords (full FBS
            // coverage, just fetched above) rather than liveSnapshot — liveSnapshot
            // at early weeks (e.g. week 2 → liveWeek=1) has thin, possibly incomplete
            // coverage, and a small/unrepresentative sample here badly distorts
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

            var result = new Dictionary<int, TeamRecord>();

            foreach (var teamRecord in currentYearTeamRecords)
            {
                // FCS teams already have an entry in currentYearTeamRecords —
                // RollingAverageService includes them, just with SeedRating/
                // TrendRating/PedigreeRating forced to 0 (a literal 0, not null).
                // Left alone, ComputeSeededAnchorUnit would read that as trendUnit=0.0
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
                    teamRecord, ZRosterScalingConstant);

                liveByTeam.TryGetValue(teamRecord.TeamID, out var live);
                int gamesPlayed = live != null ? live.Wins + live.Losses : 0;

                double liveUnit = live?.PowerRating.HasValue == true
                    ? RatingScaling.ToUnitScale((double)live.PowerRating.Value, liveMean, liveStdDev)
                    : anchorUnit; // no live data yet; gamesPlayed=0 makes this moot anyway

                double blendedUnit = _ratingBlending.BlendUnit(anchorUnit, liveUnit, gamesPlayed);
                double blendedPowerRating = RatingScaling.FromUnitScale(blendedUnit, liveMean, liveStdDev);

                // Ranking — was a raw, unblended pass-through from `live.Ranking`
                // (Finding #5: the K=4 smoothing above only ever reached PowerRating,
                // never the field CalculatePrediction's margin calc actually reads).
                // Same 2-tier logic RatingCalculator.ResolveStrength already uses
                // elsewhere, but tier 2 now reads the BLENDED PowerRating just
                // computed above instead of raw live.PowerRating — so Ranking
                // inherits the same smoothing PowerRating already gets. Tier 1
                // (real live.Ranking) still wins whenever a genuine in-season
                // value exists; only the estimate used when it doesn't (week 0/1,
                // or any week with thin live data) changes. No third SeedRating
                // fallback needed here — unlike ResolveStrength's other call
                // sites, blendedPowerRating is never null by construction (liveUnit
                // already falls back to anchorUnit above when there's no live data),
                // so tier 2 always resolves.
                decimal blendedRanking = live != null && live.Ranking > 0m
                    ? (decimal)live.Ranking
                    : 0.5m * (1m + (decimal)blendedPowerRating);

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
