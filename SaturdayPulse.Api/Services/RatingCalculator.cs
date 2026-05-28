using SaturdayPulse.Infrastructure;
using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Pure-static calculation primitives shared across the metrics pipeline.
    ///
    /// Centralizes algorithms that were previously duplicated across
    /// TeamMetricsService, WeeklyRankingsService, and GamePredictionService:
    ///
    ///   BucketWinPct            — 5% bucket rounding (×20 / 20m)
    ///   ExpectedFromPerspective — teamWinPct >= oppWinPct flip
    ///   ApplyHomeField          — IsHomeTeam/Location if-else
    ///   RivalryVarianceMultiplier        — metrics pipeline tier switch
    ///   RivalryVarianceMultiplierForDisplay — prediction display tier switch
    ///   RivalryScoringAdjustment         — scoring reduction for rivalry games
    ///   DivisionWeight          — FCS 0.25 / FBS 1.0
    ///   DampenZScore            — sign * Log(1 + |z|)
    ///   ComputeGameZScore       — full per-game Z-score pipeline
    ///   GetConferenceTier       — P4 / G5 / Independent / Other
    ///   GetDivision             — Sun Belt East/West
    ///   ConferenceDisplayOrder  — standard sort order for display
    ///
    /// All methods are stateless and unit-testable without any DI setup.
    /// </summary>
    public static class RatingCalculator
    {
        // ── Win-percentage bucketing ──────────────────────────────────────────────

        /// <summary>
        /// Rounds a raw win percentage to the nearest 2.5% increment (0.00, 0.025, 0.05 … 1.00).
        /// Used to key into the AvgScoreDeltas table, which is bucketed at 5% intervals.
        /// </summary>
        public static decimal BucketWinPct(int wins, int gamesPlayed)
            => gamesPlayed > 0
               ? Math.Round((decimal)wins / gamesPlayed * 40m, MidpointRounding.AwayFromZero) / 40m
               : 0m;

        /// <summary>
        /// Expands ranking differential space so extreme superiority relationships
        /// separate more naturally.
        /// </summary>
        public static decimal ExpandStrength(decimal ranking)
        {
            var sign = Math.Sign(ranking);
            var expanded = (decimal)Math.Pow(Math.Abs((double)ranking), 1.35);
            return Math.Round(sign * expanded, 4);
        }

        public static double GetSmoothedExpectedMargin(List<AvgScoreDifferential> buckets, decimal differential)
        {
            var closest = buckets
                .OrderBy(b => Math.Abs(b.StrengthDifferential - differential))
                .FirstOrDefault();

            if (closest == null) return 0d;
            return Math.Round((double)closest.AverageMargin, 2);
        }

        // ── Expected-margin helpers ───────────────────────────────────────────────

        public static double ExpectedFromPerspective(
            double rawExpectedDelta, decimal teamWinPct, decimal oppWinPct)
            => teamWinPct >= oppWinPct ? rawExpectedDelta : -rawExpectedDelta;

        public static double ApplyHomeField(
            double expected, bool isHomeTeam, bool isNeutral, double homeFieldAdvantage)
        {
            if (isHomeTeam) return expected + homeFieldAdvantage;
            if (isNeutral)  return expected;
            return expected - homeFieldAdvantage;
        }

        // ── Rivalry variance ──────────────────────────────────────────────────────

        public static double RivalryVarianceMultiplier(string? tier) => tier switch
        {
            "EPIC"     => 1.75,
            "NATIONAL" => 1.50,
            "STATE"    => 1.30,
            "MEH"      => 1.10,
            _          => 1.00
        };

        public static double RivalryVarianceMultiplierForDisplay(string? tier) => tier switch
        {
            "EPIC"     => 1.30,
            "NATIONAL" => 1.20,
            "STATE"    => 1.10,
            _          => 1.00
        };

        public static double RivalryScoringAdjustment(string? tier) => tier switch
        {
            "EPIC"     => 0.90,
            "NATIONAL" => 0.93,
            "STATE"    => 0.95,
            _          => 1.00
        };

        // ── Division weighting ────────────────────────────────────────────────────

        public static double DivisionWeight(string? opponentDivision)
            => opponentDivision == "FCS" ? 0.25 : 1.0;

        // ── Z-score dampening ─────────────────────────────────────────────────────

        public static double DampenZScore(double zScore)
        {
            if (zScore == 0) return 0;
            return Math.Sign(zScore) * Math.Log(1 + Math.Abs(zScore));
        }

        // ── Composite Z-score ─────────────────────────────────────────────────────

        public static double ComputeGameZScore(
            int teamPoints, int opponentPoints,
            int teamWins, int teamLosses,
            int oppWins, int oppLosses,
            bool isHomeTeam, bool isNeutralSite,
            double homeFieldAdvantage,
            IReadOnlyList<AvgScoreDelta> avgScoreDeltas,
            string? rivalryTier = null)
        {
            var teamGames = teamWins + teamLosses;
            var oppGames  = oppWins  + oppLosses;

            var teamWinPct = BucketWinPct(teamWins, teamGames);
            var oppWinPct  = BucketWinPct(oppWins,  oppGames);
            var maxWinPct  = Math.Max(teamWinPct, oppWinPct);
            var minWinPct  = Math.Min(teamWinPct, oppWinPct);

            var asd = avgScoreDeltas.FirstOrDefault(
                a => a.Team1WinPct == maxWinPct && a.Team2WinPct == minWinPct);

            if (asd == null || asd.WeightedStdDev == 0) return 0;

            var rawExpected    = (double)asd.AverageScoreDelta;
            var expected       = ExpectedFromPerspective(rawExpected, teamWinPct, oppWinPct);
            expected           = ApplyHomeField(expected, isHomeTeam, isNeutralSite, homeFieldAdvantage);

            var effectiveStDev = (double)asd.WeightedStdDev * RivalryVarianceMultiplier(rivalryTier);
            var delta          = teamPoints - opponentPoints;

            return DampenZScore((delta - expected) / effectiveStDev);
        }

        // ── Conference / team classification ──────────────────────────────────────

        /// <summary>
        /// Returns the competitive tier for a given conference string.
        /// Handles both abbreviations (SEC, B1G) and full names (Southeastern Conference).
        /// Uses bidirectional matching — checks if the conference string contains the
        /// pattern OR the pattern contains the conference string. This handles cases
        /// where the DB stores full names but callers pass abbreviations and vice versa.
        /// Team-name overrides handle edge cases (Notre Dame = P4, UConn = G5).
        /// </summary>
        public static string GetConferenceTier(string? conference, string? teamName = null)
            => teamName switch
            {
                "Notre Dame" => "P4",
                "Connecticut" => "G5",
                _ => conference switch
                {
                    "SEC"                => "P4",
                    "Big Ten"            => "P4",
                    "Big 12"             => "P4",
                    "ACC"                => "P4",
                    "American Athletic"  => "G5",
                    "Mountain West"      => "G5",
                    "Sun Belt"           => "G5",
                    "Mid-American"       => "G5",
                    "Conference USA"     => "G5",
                    _                    => "Other"
                }
            };

        /// <summary>
        /// Maps a Sun Belt team to East or West division.
        /// Returns null for all other conferences (no divisions).
        /// </summary>
        public static string? GetDivision(string teamName, string? conference)
        {
            if (conference != "Sun Belt") return null;

            var east = new HashSet<string>
            {
                "App State", "Coastal Carolina", "Georgia Southern", "Georgia State",
                "James Madison", "Marshall", "Old Dominion", "South Alabama", "Southern Miss"
            };
            return east.Contains(teamName) ? "East" : "West";
        }

        // ── Conference ordering ───────────────────────────────────────────────────

        public static int ConferenceDisplayOrder(string? conference) => conference switch
        {
            "SEC"      => 1,
            "Big Ten"  => 2,
            "ACC"      => 3,
            "Big 12"   => 4,
            "AAC"      => 5,
            "MW"       => 6,
            "MAC"      => 7,
            "C-USA"    => 8,
            "Sun Belt" => 9,
            _          => 99
        };
    }
}
