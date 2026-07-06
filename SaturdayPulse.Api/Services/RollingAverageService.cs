using Microsoft.Extensions.Options;
using SaturdayPulse.Configuration;
using SaturdayPulse.Contracts;
using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Computes the three-tier rolling averages (Seed, Trend, Pedigree) for all teams
    /// and persists the blended scalars to TeamRecords.SeedRating / TrendRating / PedigreeRating.
    ///
    /// Pipeline position: runs after UpsertFromWeeklyRankingsAsync, inside ComputeAndSaveAsync.
    ///   WeeklyRankings → UpsertFromWeeklyRankingsAsync → ComputeAndPersistAsync
    ///
    /// Tiers — all three now built on league-normalized PowerRating, not win percentage.
    /// PowerRating already incorporates W/L and SOS, and its raw scale varies far more
    /// than a [0,1] win-pct (observed range roughly -1.35 to +2.82), so every year's
    /// PowerRating is first z-scored against that year's FBS-wide mean/stddev, clamped
    /// to +-2 std devs, and mapped onto [0,1] before it enters any weighted average.
    /// This keeps Seed/Trend/Pedigree on one consistent scale across the whole 10-year
    /// lookback, whether the underlying year used the old or new calibration.
    /// ─────
    ///   Seed     (3-yr)  weights [0.50, 0.30, 0.20] — historical base only.
    ///                    ZRoster blended in on top (see below), Seed-only.
    ///   Trend    (5-yr)  weights [0.40, 0.25, 0.15, 0.12, 0.08] — pure historical, no ZRoster.
    ///   Pedigree (10-yr) linear decay (weight = n, n-1, … 1) — pure historical, no ZRoster.
    ///
    /// Roster Capacity (ZRoster) integration — Seed only
    /// ──────────────────────────────────────────────────
    ///   ZRoster (TeamRecord.ZRoster) is a national Z-score of net roster-composition
    ///   change for the CURRENT season — computed once by ComputePortalMetricsAsync
    ///   from incoming recruit/transfer talent minus departed production, coaching-
    ///   turnover penalized. It is NOT a per-historical-year signal the way PortalDelta
    ///   used to be — it describes only the season currently being seeded.
    ///
    ///   It is blended into Seed's non-live-swap branch only (i.e. only while
    ///   useLiveSwap is false), on top of the completed 3-year historical PowerRating
    ///   base, at a weight that decays by week:
    ///     Week 0-1 : 100% of ZRosterMaxBlendWeight
    ///     Week 2   :  80%
    ///     Week 3   :  60%
    ///     Week 4   :  40%
    ///     Week 5   :  20%
    ///     Week 6+  :   0% (fully decayed — by design, this lines up with
    ///                      SosWeekThreshold's default of 6, so ZRoster's influence
    ///                      has already reached zero by the time useLiveSwap fires;
    ///                      the two mechanisms don't overlap, but this is NOT enforced
    ///                      in code — if SosWeekThreshold is ever configured below 6,
    ///                      both could be active in the same week simultaneously)
    ///
    ///   Trend and Pedigree never see ZRoster at all — they are historical-only by
    ///   design, since a multi-year trailing average has no meaningful concept of
    ///   "this week's preseason roster guess."
    ///
    /// Week 6 live swap (Seed only) — UNCHANGED from prior behavior
    /// ────────────────────────────────────────────────────────────
    ///   Weeks 0-(threshold-1) : Seed = prior 3 years only (+ ZRoster blend, see above)
    ///   Week threshold+       : Seed = current season + prior 2 years, no ZRoster
    ///   Threshold driven by MetricsConfiguration.SosWeekThreshold. This is a hard,
    ///   binary switch — deliberately NOT smoothed into ZRoster's gradual decay. The
    ///   two are solving different problems: ZRoster fades out a static preseason
    ///   guess that never gets more or less true; useLiveSwap is gating on real
    ///   in-season sample size, which is genuinely noisy at low game counts and does
    ///   not behave as a linear function of weeks elapsed.
    /// </summary>
    public class RollingAverageService
    {
        private readonly IUnitOfWork          _uow;
        private readonly MetricsConfiguration _config;

        private static readonly double[] SeedWeights  = [0.50, 0.30, 0.20];
        private static readonly double[] TrendWeights = [0.40, 0.25, 0.15, 0.12, 0.08];

        // ── ZRoster blend ─────────────────────────────────────────────────────────
        // Max share of Seed's blend ZRoster can claim at full (week 0-1) strength.
        // Mirrors the old PortalDeltaBlend constant's role and value.
        private const double ZRosterMaxBlendWeight = 0.25;

        // Indexed by week (0-6). Week 0 and week 1 are both full strength — week 0 is
        // the preseason InitializeSeasonAsync snapshot, week 1 is the first real week,
        // neither has meaningfully "used up" any of the preseason estimate yet. Weeks
        // beyond the array bound (>=6) clamp to the last entry (0.0).
        private static readonly double[] ZRosterDecayByWeek = [1.0, 1.0, 0.8, 0.6, 0.4, 0.2, 0.0];

        public RollingAverageService(
            IUnitOfWork uow,
            IOptions<MetricsConfiguration> config)
        {
            _uow    = uow;
            _config = config.Value;
        }

        // ── Public record ─────────────────────────────────────────────────────────

        public record RollingAverages(
            decimal                SeedRating,
            decimal                TrendRating,
            decimal                PedigreeRating,
            IReadOnlyList<decimal> TrendHistory,
            IReadOnlyList<decimal> PedigreeHistory);

        // League-wide (FBS-only) PowerRating distribution for a single year, used to
        // normalize that year's raw PowerRating values onto a comparable [0,1] scale.
        // Public tuple shape (not a private nested type) so external callers of
        // Compute() — e.g. read-only display endpoints that recompute Seed/Trend/
        // Pedigree on the fly — can build and pass one via BuildLeagueYearStats below
        // without needing internal access to this class.
        public static IReadOnlyDictionary<short, (double Mean, double StdDev)> BuildLeagueYearStats(
            IEnumerable<TeamRecord> allTeamsAllYearsRecords,
            IReadOnlyDictionary<int, Teams> teamsDict)
        {
            // NOTE: FBS membership is determined using whatever teamsDict the caller
            // passes in — typically built from a single "current" year's team list,
            // used as a proxy for every year in the record set. A team that changed
            // divisions within the window will be mis-classified for the years that
            // don't match its current division. Same known simplification used
            // elsewhere in the pipeline (seed-week backfill's FBS-list caveat) —
            // flagged, not solved, here, and deliberately kept consistent across every
            // caller rather than fixed differently in some places and not others.
            return allTeamsAllYearsRecords
                .Where(r => teamsDict.ContainsKey(r.TeamID) &&
                            string.Equals(teamsDict[r.TeamID].Division, "fbs", StringComparison.OrdinalIgnoreCase) &&
                            r.PowerRating.HasValue)
                .GroupBy(r => r.Year)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var vals = g.Select(r => (double)r.PowerRating!.Value).ToList();
                        var mean = vals.Average();
                        var std  = vals.Count > 1
                            ? Math.Sqrt(vals.Average(v => Math.Pow(v - mean, 2)))
                            : 0.0;
                        return (Mean: mean, StdDev: std);
                    });
        }

        // ── Public API ────────────────────────────────────────────────────────────

        public async Task ComputeAndPersistAsync(
            int year,
            int? week = null,
            CancellationToken token = default)
        {
            // Load tracked records for saving — no Teams navigation property
            var currentYearRecords = await _uow.TeamRecords.GetByYearAsync(year, token);

            // Load Teams separately using correct CFBD TeamId join
            var teamIds = currentYearRecords.Select(r => r.TeamID).ToList();
            var teamsDict = await _uow.Teams.GetByTeamIdsAsync(teamIds, token);

            var historicalRecords = await _uow.TeamRecords.GetHistoricalAsync(
                fromYear: year - 10, toYearExclusive: year, token);

            var historyByTeam = historicalRecords
                .GroupBy(tr => tr.TeamID)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(r => r.Year).ToList());

            // ── League-wide PowerRating distribution, per year ─────────────────────
            var leagueStatsByYear = BuildLeagueYearStats(
                historicalRecords.Concat(currentYearRecords), teamsDict);

            bool useLiveSwap = week.HasValue && week.Value >= _config.SosWeekThreshold;

            foreach (var record in currentYearRecords)
            {
                teamsDict.TryGetValue(record.TeamID, out var team);

                if (string.Equals(team?.Division, "fcs", StringComparison.OrdinalIgnoreCase))
                {
                    record.SeedRating = 0;
                    record.TrendRating = 0;
                    record.PedigreeRating = 0;
                    continue;
                }

                historyByTeam.TryGetValue(record.TeamID, out var history);
                history ??= [];

                var averages = Compute(record, history, useLiveSwap, week, leagueStatsByYear);

                record.SeedRating = averages.SeedRating;
                record.TrendRating = averages.TrendRating;
                record.PedigreeRating = averages.PedigreeRating;
            }

            await _uow.SaveChangesAsync(token);
        }

        public RollingAverages Compute(
            TeamRecord currentRecord,
            List<TeamRecord> history,
            bool useLiveSwap,
            int? week,
            IReadOnlyDictionary<short, (double Mean, double StdDev)> leagueStatsByYear)
        {
            var seed     = ComputeSeed(currentRecord, history, useLiveSwap, week, leagueStatsByYear);
            var trend    = ComputeWeighted(history, TrendWeights, leagueStatsByYear);
            var pedigree = ComputePedigree(history, leagueStatsByYear);

            return new RollingAverages(
                SeedRating:      seed.Rating,
                TrendRating:     trend.Rating,
                PedigreeRating:  pedigree.Rating,
                TrendHistory:    trend.History,
                PedigreeHistory: pedigree.History);
        }

        // ── Tier computations ─────────────────────────────────────────────────────

        private static (decimal Rating, IReadOnlyList<decimal> History) ComputeSeed(
            TeamRecord current, List<TeamRecord> history, bool useLiveSwap, int? week,
            IReadOnlyDictionary<short, (double Mean, double StdDev)> leagueStatsByYear)
        {
            if (useLiveSwap)
            {
                // Unchanged structure from before: current season's own in-progress
                // PowerRating (now normalized, was win-pct) becomes the top-weighted
                // slot, prior lookback shrinks from 3 years to 2. No ZRoster here —
                // by the time this branch is live, ZRoster has already decayed to 0
                // under the default SosWeekThreshold of 6 (see class-level caveat).
                var values = new List<double> { NormalizePowerRating(current, leagueStatsByYear) };
                values.AddRange(history.Take(2).Select(r => NormalizePowerRating(r, leagueStatsByYear)));
                return (ApplyWeights(values, SeedWeights), []);
            }
            else
            {
                var values = history.Take(3)
                    .Select(r => NormalizePowerRating(r, leagueStatsByYear))
                    .ToList();

                var baseRating = (double)ApplyWeights(values, SeedWeights);
                var blended    = BlendWithZRoster(baseRating, current.ZRoster, week);
                return ((decimal)Math.Round(blended, 4), []);
            }
        }

        private static (decimal Rating, IReadOnlyList<decimal> History) ComputeWeighted(
            List<TeamRecord> history, double[] weights,
            IReadOnlyDictionary<short, (double Mean, double StdDev)> leagueStatsByYear)
        {
            // Trend is pure historical — no ZRoster. A 5-year trailing average has no
            // meaningful concept of "this week's preseason roster guess."
            var records = history.Take(weights.Length).ToList();
            var values  = records.Select(r => NormalizePowerRating(r, leagueStatsByYear)).ToList();
            var rating  = ApplyWeights(values, weights);
            var hist    = values.Select(v => (decimal)Math.Round(v, 4)).ToList();

            return (rating, hist);
        }

        private static (decimal Rating, IReadOnlyList<decimal> History) ComputePedigree(
            List<TeamRecord> history,
            IReadOnlyDictionary<short, (double Mean, double StdDev)> leagueStatsByYear)
        {
            // Pedigree is pure historical, same reasoning as Trend — no ZRoster.
            var records = history.Take(10).ToList();
            int n       = records.Count;

            if (n == 0) return (0m, []);

            long   weightSum = (long)n * (n + 1) / 2;
            double total     = 0.0;
            var    hist      = new List<decimal>(n);

            for (int i = 0; i < n; i++)
            {
                double normalizedPr = NormalizePowerRating(records[i], leagueStatsByYear);
                int    weight       = n - i;
                total += normalizedPr * weight;
                hist.Add((decimal)Math.Round(normalizedPr, 4));
            }

            return ((decimal)Math.Round(total / weightSum, 4), hist);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Z-scores a team's PowerRating against its year's FBS-wide mean/stddev, then
        /// maps the result onto [0,1] the same way the old portal signal was mapped —
        /// clamp to +-2 std devs, then 0.5 + (clamped / 4.0). Falls back to a neutral
        /// 0.5 if there's no league distribution for that year (e.g. missing data) or
        /// the record itself has no PowerRating yet.
        /// </summary>
        private static double NormalizePowerRating(
            TeamRecord r, IReadOnlyDictionary<short, (double Mean, double StdDev)> leagueStatsByYear)
        {
            if (!r.PowerRating.HasValue) return 0.5;
            if (!leagueStatsByYear.TryGetValue(r.Year, out var stats) || stats.StdDev <= 0)
                return 0.5;

            var z = ((double)r.PowerRating.Value - stats.Mean) / stats.StdDev;
            return ToUnitScale(z);
        }

        /// <summary>
        /// Clamp a z-score to +-2 std devs and map onto [0,1], centered at 0.5.
        /// Shared by PowerRating normalization and ZRoster's own blend mapping so both
        /// terms in any blend are on the identical scale.
        /// </summary>
        private static double ToUnitScale(double z)
        {
            var clamped = Math.Max(-2.0, Math.Min(2.0, z));
            return 0.5 + (clamped / 4.0);
        }

        /// <summary>
        /// Returns ZRoster's blend weight fraction for a given week, per the agreed
        /// decay schedule (100/80/60/40/20/0 across weeks 0-6). Null week (e.g. the
        /// year-end call from BackfillYearAsync) is treated as fully decayed — there is
        /// no meaningful "preseason" concept once a full season has already been run.
        /// </summary>
        private static double ZRosterDecayFactor(int? week)
        {
            if (!week.HasValue) return 0.0;
            var idx = Math.Clamp(week.Value, 0, ZRosterDecayByWeek.Length - 1);
            return ZRosterDecayByWeek[idx];
        }

        /// <summary>
        /// Blends ZRoster into an already-computed base value. Unlike the old
        /// BlendWithPortal (fixed 75/25 split, applied per historical year), this is a
        /// single post-hoc adjustment applied once to the completed 3-year historical
        /// Seed base, with a blend weight that itself shrinks by week
        /// (ZRosterMaxBlendWeight * decayFactor) rather than a fixed split. This is
        /// deliberate (Approach B from the design discussion): it's the only way the
        /// blend actually reaches the pure base value at week 6, rather than
        /// permanently diluting it by a fixed fraction forever.
        /// </summary>
        private static double BlendWithZRoster(double baseValue, decimal? zRoster, int? week)
        {
            if (!zRoster.HasValue) return baseValue;

            var decay = ZRosterDecayFactor(week);
            if (decay <= 0) return baseValue;

            var zRosterSignal = ToUnitScale((double)zRoster.Value);
            var rosterWeight  = ZRosterMaxBlendWeight * decay;
            var baseWeight    = 1.0 - rosterWeight;

            return (baseValue * baseWeight) + (zRosterSignal * rosterWeight);
        }

        private static decimal ApplyWeights(List<double> values, double[] weights)
        {
            if (values.Count == 0) return 0m;

            int    n         = Math.Min(values.Count, weights.Length);
            double weightSum = 0.0;
            double total     = 0.0;

            for (int i = 0; i < n; i++)
            {
                weightSum += weights[i];
                total     += values[i] * weights[i];
            }

            return weightSum > 0 ? (decimal)Math.Round(total / weightSum, 4) : 0m;
        }
    }
}
