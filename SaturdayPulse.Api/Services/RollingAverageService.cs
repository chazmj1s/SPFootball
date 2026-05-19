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
    /// Pipeline position: runs after updateTeamRecords, before setSOS.
    ///   updateTeamRecords → calculateRollingAverages → setSOS → calculatePowerRatings → ...
    ///
    /// Tiers
    /// ─────
    ///   Seed     (3-yr)  weights [0.50, 0.30, 0.20]  — internal pipeline only, not user-facing
    ///   Trend    (5-yr)  weights [0.40, 0.25, 0.15, 0.12, 0.08]
    ///   Pedigree (10-yr) linear decay (weight = n, n-1, … 1)
    ///
    /// Week 6 live swap (Seed only)
    /// ────────────────────────────
    ///   Weeks 0-(threshold-1) : Seed = (Year-1×50%) + (Year-2×30%) + (Year-3×20%)
    ///   Week threshold+       : Seed = (CurrentSeason×50%) + (Year-1×30%) + (Year-2×20%)
    ///   Threshold driven by MetricsConfiguration.SosWeekThreshold.
    /// </summary>
    public class RollingAverageService
    {
        private readonly IUnitOfWork          _uow;
        private readonly MetricsConfiguration _config;

        private static readonly double[] SeedWeights  = [0.50, 0.30, 0.20];
        private static readonly double[] TrendWeights = [0.40, 0.25, 0.15, 0.12, 0.08];

        private const double ExtraWinBump = 0.25;

        public RollingAverageService(
            IUnitOfWork uow,
            IOptions<MetricsConfiguration> config)
        {
            _uow    = uow;
            _config = config.Value;
        }

        // ── Public record ─────────────────────────────────────────────────────────

        /// <summary>
        /// Full result for one team/year — blended scalars plus constituent history arrays.
        /// Seed has no history array (internal pipeline value only).
        /// trendHistory and pedigreeHistory are normalized win percentages, most-recent first.
        /// </summary>
        public record RollingAverages(
            decimal                SeedRating,
            decimal                TrendRating,
            decimal                PedigreeRating,
            IReadOnlyList<decimal> TrendHistory,
            IReadOnlyList<decimal> PedigreeHistory);

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Computes all three tiers for every FBS team and writes the blended scalars
        /// to TeamRecords. FCS teams are left null.
        /// </summary>
        public async Task ComputeAndPersistAsync(
            int year,
            int? week = null,
            CancellationToken token = default)
        {
            var currentYearRecords = await _uow.TeamRecords.GetByYearWithTeamsAsync(year, token);

            var historicalRecords  = await _uow.TeamRecords.GetHistoricalAsync(
                fromYear: year - 10, toYearExclusive: year, token);

            var historyByTeam = historicalRecords
                .GroupBy(tr => tr.TeamID)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(r => r.Year).ToList());

            bool useLiveSwap = week.HasValue && week.Value >= _config.SosWeekThreshold;

            foreach (var record in currentYearRecords)
            {
                if (record.Teams?.Division == "FCS")
                {
                    record.SeedRating     = 0;
                    record.TrendRating    = 0;
                    record.PedigreeRating = 0;
                    continue;
                }

                historyByTeam.TryGetValue(record.TeamID, out var history);
                history ??= [];

                var averages = Compute(record, history, useLiveSwap);

                record.SeedRating     = averages.SeedRating;
                record.TrendRating    = averages.TrendRating;
                record.PedigreeRating = averages.PedigreeRating;
                // TrendHistory and PedigreeHistory are not persisted — API only
            }

            await _uow.SaveChangesAsync(token);
        }

        /// <summary>
        /// Computes all three tiers for a single team without hitting the database.
        /// Returns blended scalars plus trendHistory (5 values) and pedigreeHistory (10 values),
        /// all most-recent first.
        /// </summary>
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
                values = [WinPct(current)];
                values.AddRange(history.Take(2).Select(NormalizeWinPct));
            }
            else
            {
                values = history.Take(3).Select(NormalizeWinPct).ToList();
            }

            return (ApplyWeights(values, SeedWeights), []);
        }

        private static (decimal Rating, IReadOnlyList<decimal> History) ComputeWeighted(
            List<TeamRecord> history, double[] weights)
        {
            var records = history.Take(weights.Length).ToList();
            var values  = records.Select(NormalizeWinPct).ToList();
            var rating  = ApplyWeights(values, weights);
            var hist    = values.Select(v => (decimal)Math.Round(v, 4)).ToList();

            return (rating, hist);
        }

        private static (decimal Rating, IReadOnlyList<decimal> History) ComputePedigree(
            List<TeamRecord> history)
        {
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
