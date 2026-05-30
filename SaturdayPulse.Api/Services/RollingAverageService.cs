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
    /// Tiers
    /// ─────
    ///   Seed     (3-yr)  weights [0.50, 0.30, 0.20]
    ///   Trend    (5-yr)  weights [0.40, 0.25, 0.15, 0.12, 0.08]
    ///   Pedigree (10-yr) linear decay (weight = n, n-1, … 1)
    ///
    /// Portal integration (Seed and Trend only)
    /// ────────────────────────────────────────
    ///   PortalDelta from TeamRecord is blended into Seed and Trend alongside
    ///   win percentage. Weight: 25% portal, 75% win percentage.
    ///   Years without portal data (pre-2021) use win percentage only.
    ///   Pedigree is win-percentage only — portal data doesn't go back far enough.
    ///
    /// Week 6 live swap (Seed only)
    /// ────────────────────────────
    ///   Weeks 0-(threshold-1) : Seed = prior years only
    ///   Week threshold+       : Seed = current season + prior years
    ///   Threshold driven by MetricsConfiguration.SosWeekThreshold.
    /// </summary>
    public class RollingAverageService
    {
        private readonly IUnitOfWork          _uow;
        private readonly MetricsConfiguration _config;

        private static readonly double[] SeedWeights  = [0.50, 0.30, 0.20];
        private static readonly double[] TrendWeights = [0.40, 0.25, 0.15, 0.12, 0.08];

        private const double ExtraWinBump    = 0.25;
        private const double PortalWinBlend  = 0.75; // win pct share
        private const double PortalDeltaBlend = 0.25; // portal delta share

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

                var averages = Compute(record, history, useLiveSwap);

                record.SeedRating = averages.SeedRating;
                record.TrendRating = averages.TrendRating;
                record.PedigreeRating = averages.PedigreeRating;
            }

            await _uow.SaveChangesAsync(token);
        }
        public RollingAverages Compute(
            TeamRecord currentRecord,
            List<TeamRecord> history,
            bool useLiveSwap)
        {
            var seed     = ComputeSeed(currentRecord, history, useLiveSwap);
            var trend    = ComputeWeighted(history, TrendWeights);
            var pedigree = ComputePedigree(history);

            return new RollingAverages(
                SeedRating:      seed.Rating,
                TrendRating:     trend.Rating,
                PedigreeRating:  pedigree.Rating,
                TrendHistory:    trend.History,
                PedigreeHistory: pedigree.History);
        }

        // ── Tier computations ─────────────────────────────────────────────────────

        private static (decimal Rating, IReadOnlyList<decimal> History) ComputeSeed(
            TeamRecord current, List<TeamRecord> history, bool useLiveSwap)
        {
            List<double> values;

            if (useLiveSwap)
            {
                values = [BlendWithPortal(WinPct(current), current.PortalDelta)];
                values.AddRange(history.Take(2).Select(r => BlendWithPortal(NormalizeWinPct(r), r.PortalDelta)));
            }
            else
            {
                values = history.Take(3).Select(r => BlendWithPortal(NormalizeWinPct(r), r.PortalDelta)).ToList();
            }

            return (ApplyWeights(values, SeedWeights), []);
        }

        private static (decimal Rating, IReadOnlyList<decimal> History) ComputeWeighted(
            List<TeamRecord> history, double[] weights)
        {
            var records = history.Take(weights.Length).ToList();
            var values  = records.Select(r => BlendWithPortal(NormalizeWinPct(r), r.PortalDelta)).ToList();
            var rating  = ApplyWeights(values, weights);
            var hist    = values.Select(v => (decimal)Math.Round(v, 4)).ToList();

            return (rating, hist);
        }

        private static (decimal Rating, IReadOnlyList<decimal> History) ComputePedigree(
            List<TeamRecord> history)
        {
            // Pedigree is win-percentage only — portal data doesn't go back far enough
            // to be meaningful over a 10-year window.
            var records = history.Take(10).ToList();
            int n       = records.Count;

            if (n == 0) return (0m, []);

            long   weightSum = (long)n * (n + 1) / 2;
            double total     = 0.0;
            var    hist      = new List<decimal>(n);

            for (int i = 0; i < n; i++)
            {
                double winPct = NormalizeWinPct(records[i]);
                int    weight = n - i;
                total += winPct * weight;
                hist.Add((decimal)Math.Round(winPct, 4));
            }

            return ((decimal)Math.Round(total / weightSum, 4), hist);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Blends win percentage with portal delta signal.
        /// For years without portal data (PortalDelta is null), uses win pct only.
        /// Portal delta is normalized on a Z-score scale — convert to [0,1] range
        /// for blending with win percentage using sigmoid-like clamping.
        /// </summary>
        private static double BlendWithPortal(double winPct, decimal? portalDelta)
        {
            if (!portalDelta.HasValue)
                return winPct;

            // PortalDelta is a Z-score (mean=0, std=1). Convert to a 0-1 scale
            // centered at 0.5 so it blends naturally with win percentage.
            // Clamp to ±2 std devs to prevent extreme outliers dominating.
            var clampedDelta = Math.Max(-2.0, Math.Min(2.0, (double)portalDelta.Value));
            var portalSignal = 0.5 + (clampedDelta / 4.0); // maps [-2,+2] → [0,1]

            return Math.Round(
                (winPct       * PortalWinBlend) +
                (portalSignal * PortalDeltaBlend), 4);
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

        private static double NormalizeWinPct(TeamRecord r)
        {
            int totalGames    = r.Wins + r.Losses;
            int standardGames = r.RegularSeasonGames;

            if (totalGames <= 0) return 0.0;

            if (totalGames <= standardGames)
                return (double)r.Wins / totalGames;

            int baseWins  = Math.Min(r.Wins, standardGames);
            int extraWins = Math.Max(0, r.Wins - standardGames);
            return (baseWins + extraWins * ExtraWinBump) / standardGames;
        }

        private static double WinPct(TeamRecord r)
        {
            int total = r.Wins + r.Losses;
            return total > 0 ? (double)r.Wins / total : 0.0;
        }
    }
}
