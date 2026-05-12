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
        /// Rounds a raw win percentage to the nearest 5% increment (0.00, 0.05 … 1.00).
        /// Used to key into the AvgScoreDeltas table, which is bucketed at 5% intervals.
        /// </summary>
        public static decimal BucketWinPct(int wins, int gamesPlayed)
            => gamesPlayed > 0
               ? Math.Round((decimal)wins / gamesPlayed * 20m, MidpointRounding.AwayFromZero) / 20m
               : 0m;

        // ── Expected-margin helpers ───────────────────────────────────────────────

        /// <summary>
        /// Flips the raw expected margin (always from the higher-win-pct team's
        /// perspective) so that it reads from <paramref name="teamWinPct"/>'s perspective.
        /// </summary>
        public static double ExpectedFromPerspective(
            double rawExpectedDelta, decimal teamWinPct, decimal oppWinPct)
            => teamWinPct >= oppWinPct ? rawExpectedDelta : -rawExpectedDelta;

        /// <summary>
        /// Adjusts an expected margin for home-field advantage.
        /// Home team → add HFA. Neutral site → no adjustment. Away → subtract HFA.
        /// </summary>
        public static double ApplyHomeField(
            double expected, bool isHomeTeam, bool isNeutral, double homeFieldAdvantage)
        {
            if (isHomeTeam) return expected + homeFieldAdvantage;
            if (isNeutral)  return expected;
            return expected - homeFieldAdvantage;
        }

        // ── Rivalry variance ──────────────────────────────────────────────────────

        /// <summary>
        /// Rivalry variance multiplier for the metrics pipeline (TeamMetrics, WeeklyRankings).
        /// Applied to AvgScoreDeltas.StDevP to increase prediction uncertainty.
        /// </summary>
        public static double RivalryVarianceMultiplier(string? tier) => tier switch
        {
            "EPIC"     => 1.75,
            "NATIONAL" => 1.50,
            "STATE"    => 1.30,
            "MEH"      => 1.10,
            _          => 1.00
        };

        /// <summary>
        /// Rivalry variance multiplier for GamePredictionService display output
        /// (margin-of-error and confidence bands). More conservative than the
        /// metrics multiplier — kept separate because they serve different purposes.
        /// </summary>
        public static double RivalryVarianceMultiplierForDisplay(string? tier) => tier switch
        {
            "EPIC"     => 1.30,
            "NATIONAL" => 1.20,
            "STATE"    => 1.10,
            _          => 1.00
        };

        /// <summary>
        /// Scoring-reduction multiplier for rivalry games applied to projected scores.
        /// Reflects the tendency for rivalry games to be lower-scoring defensive battles.
        /// </summary>
        public static double RivalryScoringAdjustment(string? tier) => tier switch
        {
            "EPIC"     => 0.90,
            "NATIONAL" => 0.93,
            "STATE"    => 0.95,
            _          => 1.00
        };

        // ── Division weighting ────────────────────────────────────────────────────

        /// <summary>
        /// Returns the opponent division weight for Z-score and SOS calculations.
        /// FCS opponents count at 25% to prevent metric inflation against weaker opponents.
        /// </summary>
        public static double DivisionWeight(string? opponentDivision)
            => opponentDivision == "FCS" ? 0.25 : 1.0;

        // ── Z-score dampening ─────────────────────────────────────────────────────

        /// <summary>
        /// Applies logarithmic dampening: sign(z) × log(1 + |z|).
        /// Reflects diminishing returns — a 40-point blowout is not twice as
        /// meaningful as a 20-point blowout.
        /// </summary>
        public static double DampenZScore(double zScore)
        {
            if (zScore == 0) return 0;
            return Math.Sign(zScore) * Math.Log(1 + Math.Abs(zScore));
        }

        // ── Composite Z-score ─────────────────────────────────────────────────────

        /// <summary>
        /// Calculates the composite per-game Z-score for one team from one game:
        ///   1. Look up expected margin from AvgScoreDeltas bucket.
        ///   2. Flip to team perspective.
        ///   3. Adjust for home field.
        ///   4. Adjust for rivalry variance.
        ///   5. Compute (actual - expected) / effectiveStDev.
        ///   6. Apply logarithmic dampening.
        /// Returns 0 if no matching ASD bucket or StDevP is 0.
        /// </summary>
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

            if (asd == null || asd.StDevP == 0) return 0;

            var rawExpected    = (double)asd.AverageScoreDelta;
            var expected       = ExpectedFromPerspective(rawExpected, teamWinPct, oppWinPct);
            expected           = ApplyHomeField(expected, isHomeTeam, isNeutralSite, homeFieldAdvantage);

            var effectiveStDev = (double)asd.StDevP * RivalryVarianceMultiplier(rivalryTier);
            var delta          = teamPoints - opponentPoints;

            return DampenZScore((delta - expected) / effectiveStDev);
        }

        // ── Conference / team classification ──────────────────────────────────────

        /// <summary>
        /// Returns the competitive tier for a given conference string.
        /// Handles both abbreviations and full names.
        /// Team-name overrides handle edge cases (Notre Dame = P4, UConn = G5).
        /// </summary>
        public static string GetConferenceTier(string? conference, string? teamName = null)
        {
            if (!string.IsNullOrEmpty(teamName))
            {
                if (teamName.Equals("Notre Dame",  StringComparison.OrdinalIgnoreCase)) return "P4";
                if (teamName.Equals("Connecticut", StringComparison.OrdinalIgnoreCase)) return "G5";
            }

            if (string.IsNullOrEmpty(conference)) return "Other";

            var power4 = new[]
            {
                "SEC", "Southeastern Conference",
                "Big Ten", "Big Ten Conference",
                "Big 12", "Big 12 Conference",
                "ACC", "Atlantic Coast Conference"
            };
            if (power4.Any(p => conference.Contains(p, StringComparison.OrdinalIgnoreCase))) return "P4";

            var group5 = new[]
            {
                "American Athletic", "American Athletic Conference", "AAC",
                "Mountain West", "Mountain West Conference",
                "Sun Belt", "Sun Belt Conference",
                "Mid-American", "Mid-American Conference", "MAC",
                "Conference USA", "C-USA",
                "Pac-12", "Pac-12 Conference"
            };
            if (group5.Any(g => conference.Contains(g, StringComparison.OrdinalIgnoreCase))) return "G5";
            if (conference.Contains("Independent", StringComparison.OrdinalIgnoreCase)) return "Independent";

            return "Other";
        }

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

        /// <summary>
        /// Sort key for standard conference display order across all endpoints.
        /// </summary>
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
