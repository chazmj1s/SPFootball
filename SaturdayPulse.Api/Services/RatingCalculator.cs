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
    ///   RivalryVarianceMultiplierForDisplay — prediction display, data-driven ratio
    ///   RivalryScoringAdjustment         — prediction display, data-driven ratio
    ///   DivisionWeight          — FCS 0.25 / FBS 1.0
    ///   DampenZScore            — sign * Log(1 + |z|)
    ///   ComputeGameZScore       — full per-game Z-score pipeline
    ///   GetConferenceTier       — P4 / G5 / Independent / Other
    ///   GetDivision             — Sun Belt East/West
    ///   ConferenceDisplayOrder  — standard sort order for display
    ///
    /// All methods are stateless and unit-testable without any DI setup.
    ///
    /// ── REBUILT — RivalryVarianceMultiplierForDisplay / RivalryScoringAdjustment ──
    ///   Both previously keyed off RivalryTier (EPIC/NATIONAL/STATE — hand-picked
    ///   constants with no data behind them: 1.75/1.50/1.30 and 0.90/0.93/0.95).
    ///   Replaced with a real ratio against MatchupHistory (Layer 1/2: AvgTotalPoints
    ///   column added, MatchupHistoryCalculator backfilled from actual game data for
    ///   the 50 curated rivalry pairs) compared against the live, interpolated
    ///   AvgScoreDifferential values for the pair's current strength differential.
    ///
    ///   For a known rivalry, RivalryVarianceMultiplierForDisplay reduces cleanly to
    ///   just using that pair's own real historical StdDevMargin directly as the
    ///   effective stddev (see GamePredictionService: stdDev = distribution.StdDev *
    ///   multiplier = distribution.StdDev * (rivalry.StDevMargin /
    ///   distribution.StdDev) = rivalry.StDevMargin). For a non-rivalry pairing
    ///   (rivalry == null — true for anything outside the 50 curated pairs, e.g.
    ///   Texas/Alabama 2025), the multiplier is 1.00 — not a guess, the correct
    ///   "no specific information, use the generic differential-based baseline"
    ///   default.
    ///
    ///   RivalryVarianceMultiplier (no "ForDisplay" suffix) is UNTOUCHED — confirmed
    ///   via reference search that it's also called by RatingCalculator.
    ///   ComputeGameZScore, TeamMetricsService, and WeeklyRankingsService directly —
    ///   i.e. it feeds the live weekly rating computation, not just prediction
    ///   display. Changing its behavior belongs with the already-planned calc-engine
    ///   refactor (WeeklyRankingsService/RollingAverageService/etc.), not this pass.
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

        // ── Rivalry variance (metrics pipeline — UNTOUCHED) ─────────────────────────

        /// <summary>
        /// Data-driven rivalry variance multiplier. When a curated MatchupHistory row
        /// exists for this pair, this reduces to using that pair's own real StDevMargin
        /// directly as the effective stddev (multiplier = pairStdDev / bucketStdDev).
        /// Falls back to 1.0 (base stddev stands unmodified) when no curated data
        /// exists for the pair.
        /// </summary>
        public static double RivalryVarianceMultiplier(MatchupHistory? matchup, double bucketStdDev)
        {
            if (matchup == null || matchup.StDevMargin <= 0 || bucketStdDev <= 0)
                return 1.0;

            return (double)matchup.StDevMargin / bucketStdDev;
        }

        // ── Rivalry variance / scoring (prediction display — REBUILT) ───────────────

        /// <summary>
        /// Data-driven replacement for the old EPIC/NATIONAL/STATE tier switch.
        /// Returns the ratio of this specific pair's real historical StDevMargin
        /// (MatchupHistoryCalculator, backfilled from actual game data for the 50
        /// curated rivalries) to the live, interpolated expected stddev for their
        /// current strength differential (AvgScoreDifferential, via
        /// AvgScoreDifferentialService).
        ///
        /// Returns 1.00 (no adjustment) when the pair isn't one of the 50 curated
        /// rivalries, or when there's not enough data to trust a ratio — both
        /// correct "no specific information" defaults, not guesses.
        /// </summary>
        public static double RivalryVarianceMultiplierForDisplay(
            MatchupHistory? rivalry, double expectedStdDev)
        {
            if (rivalry == null || rivalry.GamesPlayed <= 0 || expectedStdDev <= 0)
                return 1.00;

            return (double)rivalry.StDevMargin / expectedStdDev;
        }

        /// <summary>
        /// Data-driven replacement for the old EPIC/NATIONAL/STATE scoring-reduction
        /// tier switch. Returns the ratio of this specific pair's real historical
        /// AvgTotalPoints (MatchupHistoryCalculator, same backfill as above) to the
        /// live, interpolated expected total points for their current strength
        /// differential (AvgScoreDifferential.AverageTotalPoints).
        ///
        /// Returns 1.00 (no adjustment) under the same "not a curated rivalry, or not
        /// enough data" conditions as RivalryVarianceMultiplierForDisplay.
        /// </summary>
        public static double RivalryScoringAdjustment(
            MatchupHistory? rivalry, double expectedTotalPoints)
        {
            if (rivalry == null || rivalry.GamesPlayed <= 0 || expectedTotalPoints <= 0)
                return 1.00;

            return (double)rivalry.AvgTotalPoints / expectedTotalPoints;
        }

        // ── Division weighting ────────────────────────────────────────────────────

        public static double DivisionWeight(string? opponentDivision)
            => opponentDivision == "FCS" ? 0.25 : 1.0;

        // ── Z-score dampening ─────────────────────────────────────────────────────

        public static double DampenZScore(double zScore)
        {
            if (zScore == 0) return 0;
            return Math.Sign(zScore) * Math.Log(1 + Math.Abs(zScore));
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
