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
    ///   ComputeRanking          — WinPct × (1 + PowerRating), single source of truth
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
    ///
    /// ── ADDED — ComputeRanking, single source of truth ───────────────────────────
    ///   Previously WeeklyRankingsService inlined `winPct × (1 + PowerRating)`
    ///   directly, and ExperimentalInertiaRatingService inlined its own separate
    ///   0.5m * (1 + PowerRating) estimate as a fallback. Both are now expected to
    ///   call this method instead, so there's exactly one implementation of the
    ///   Ranking formula in the codebase. Callers are responsible for deciding
    ///   which wins/losses to pass in (actual-only vs. full-season actual+projected
    ///   — see WeeklyRankingsService Step 10 remarks for why full-season is now the
    ///   correct choice there).
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
            if (isNeutral) return expected;
            if (isHomeTeam) return expected + homeFieldAdvantage;
            return expected - homeFieldAdvantage;
        }

        // ── Opponent pregame strength resolution ────────────────────────────────

        /// <summary>
        /// Resolves a team's pregame strength for use as an opponent-strength input
        /// to SOS. Three-tier fallback: real prior-week Ranking → PowerRating-derived
        /// estimate → raw preseason SeedRating (already on the Ranking scale, not the
        /// PowerRating scale) when no prior WeeklyRankings row exists at all — e.g.
        /// week 0 of a new season, where `prior` is always null by design.
        /// </summary>
        public static decimal ResolveStrength(int teamId, WeeklyRanking? prior, IReadOnlyDictionary<int, decimal> seedByTeamId)
        {
            if (prior != null && prior.Ranking > 0m) return (decimal)prior.Ranking;
            if (prior?.PowerRating != null) return 0.5m * (1m + prior.PowerRating.Value);
            return seedByTeamId.TryGetValue(teamId, out var seed) ? seed : 0m;
        }

        // ── Per-game Z-score — single source of truth ─────────────────────────────

        /// <summary>
        /// Result of ComputeGameZScore — everything WeeklyRankingsService step 5 needs
        /// per game, plus everything a diagnostic (DeveloperService.
        /// AnalyzeTeamGameZScoresAsync) needs to inspect the same computation directly.
        /// </summary>
        public record GameZScoreResult(
            double ZScore, double OffZScore, double DefZScore,
            double DivWeight, double QualityMod, decimal OppPregameStrength);

        /// <summary>
        /// Full per-game Z-score pipeline — expected margin, composite/offensive/
        /// defensive Z-scores, division weight, quality-of-win modifier, and the
        /// opponent's pregame strength for SOS. Single source of truth: previously
        /// duplicated inline in WeeklyRankingsService step 5 (added here 2026-09-02,
        /// finishing the extraction this file's class header already described).
        ///
        /// leagueAvgStrength is the caller-supplied expanded-strength value an
        /// average FBS team presents this week (see WeeklyRankingsService remarks,
        /// 2026-09-02 fix) — the team's OWN prior strength is deliberately not an
        /// input here; only the opponent's strength and the league baseline set the
        /// expectation bar.
        /// </summary>
        public static GameZScoreResult ComputeGameZScore(
            int opponentId,
            string? opponentDivision,
            char location,
            bool isHomeTeam,
            int teamPoints,
            int opponentPoints,
            decimal leagueAvgStrength,
            WeeklyRanking? oppPrior,
            IReadOnlyDictionary<int, decimal> seedByTeamId,
            List<AvgScoreDifferential> avgScoreDifferentials,
            MatchupHistory? matchup,
            double homeFieldAdvantage,
            double leagueAvgScore)
        {
            var oppStrength = ExpandStrength(ResolveStrength(opponentId, oppPrior, seedByTeamId));
            // Differential anchored to a league-average team, not the team's own
            // prior rating (see WeeklyRankingsService step 5 remarks, 2026-09-02).
            var rawDiff      = leagueAvgStrength - oppStrength;
            var clampedDiff  = Math.Max(-3.0m, Math.Min(3.0m, rawDiff));
            var differential = Math.Round(clampedDiff / 0.05m, MidpointRounding.AwayFromZero) * 0.05m;

            var bucket = GetSmoothedExpectedMargin(avgScoreDifferentials, differential);

            double zScore = 0.0, offZScore = 0.0, defZScore = 0.0;

            // bucket is already from team's perspective (positive = team favored)
            var expectedFromTeam = (double)bucket;
            expectedFromTeam     = ApplyHomeField(expectedFromTeam, isHomeTeam, location == 'N', homeFieldAdvantage);

            // Get StdDev from the differential bucket.
            var bucketRow = avgScoreDifferentials
                .OrderBy(b => Math.Abs(b.StrengthDifferential - differential))
                .FirstOrDefault();

            var baseStdDev      = bucketRow != null ? (double)bucketRow.StdDevMargin : 14.0;
            var effectiveStDev  = baseStdDev * RivalryVarianceMultiplier(matchup, baseStdDev);

            if (effectiveStDev > 0)
            {
                var delta = teamPoints - opponentPoints;
                zScore    = DampenZScore((delta - expectedFromTeam) / effectiveStDev);

                var expectedTeamScore = leagueAvgScore + (expectedFromTeam / 2.0);
                var expectedOppScore  = leagueAvgScore - (expectedFromTeam / 2.0);

                offZScore = DampenZScore((teamPoints    - expectedTeamScore) / effectiveStDev);
                defZScore = DampenZScore((expectedOppScore - opponentPoints) / effectiveStDev);
            }

            var divWeight = DivisionWeight(opponentDivision);

            // Smooth quality-of-win modifier — replaces the four-bucket step.
            //   QualityMod = clamp(1 + z * 0.25, 0.50, 1.50)
            // Applied to the team's own z-score in PowerRating, NOT to SOS.
            var qualityMod = Math.Max(0.50, Math.Min(1.50, 1.0 + (zScore * 0.25)));

            // Pregame opponent strength for the new SOS calc.
            // Chain: WeeklyRankings[opponent, week-1].Ranking → SeedRating → 0
            // FCS opponents (and any opponent we can't find) get 0 strength.
            decimal oppPregameStrength = 0m;
            bool oppIsFcs = string.Equals(opponentDivision, "fcs", StringComparison.OrdinalIgnoreCase);
            if (!oppIsFcs)
                oppPregameStrength = ResolveStrength(opponentId, oppPrior, seedByTeamId);

            return new GameZScoreResult(zScore, offZScore, defZScore, divWeight, qualityMod, oppPregameStrength);
        }

        /// <summary>
        /// Aggregates a team's per-game Z-scores into a single season AvgZScore —
        /// a QualityMod/DivWeight-weighted mean, NOT a plain average. QualityMod is
        /// meant to make decisive games count MORE toward the average, which means
        /// the denominator must use the same QualityMod*DivWeight weight the
        /// numerator does — otherwise QualityMod isn't weighting anything, it's
        /// inflating the whole average's scale, unbounded by the team's own actual
        /// z-score range.
        ///
        /// FIXED 2026-09-02 (Georgia/Notre Dame gap, part 2 — QualityMod). The
        /// previous inline aggregation (WeeklyRankingsService step 9) used
        /// QualityMod only in the numerator — Sum(ZScore*QualityMod*DivWeight) /
        /// Sum(DivWeight) — so a team with more decisive (higher |z|) games had its
        /// ENTIRE average pushed up by more than its performance distribution
        /// justified, independent of opponent strength. Confirmed against real 2025
        /// per-game data (DeveloperService.AnalyzeTeamGameZScoresAsync): properly
        /// normalizing brought both Georgia's and Notre Dame's average back toward
        /// their plain (unweighted) mean and shrank the gap between them by ~27%,
        /// without changing what QualityMod measures or removing the "decisive
        /// games matter more" intent — it just makes this an actual weighted
        /// average instead of an unbounded amplification.
        /// </summary>
        public static double ComputeWeightedAvgZScore(
            IEnumerable<(double ZScore, double QualityMod, double DivWeight)> games)
        {
            double weightSum = 0.0, numerator = 0.0;
            foreach (var g in games)
            {
                var weight = g.QualityMod * g.DivWeight;
                weightSum += weight;
                numerator += g.ZScore * weight;
            }
            return weightSum > 0 ? numerator / weightSum : 0.0;
        }

        // ── Ranking — single source of truth ─────────────────────────────────────

        /// <summary>
        /// Ranking = WinPct × (1 + PowerRating). Returns null when wins+losses == 0
        /// (no record to compute a WinPct from — e.g. week 0 preseason, where
        /// Ranking is intentionally left undefined per Charlie).
        ///
        /// Callers decide what "wins"/"losses" means for their context — the
        /// production path (WeeklyRankingsService) passes the full-season
        /// actual+projected rollup so Ranking reflects the whole season, not just
        /// games played/locked through the current week (see WeeklyRankingsService
        /// Step 10 remarks). This method itself is agnostic to that choice; it just
        /// applies the formula once, consistently, wherever it's called.
        /// </summary>
        public static decimal? ComputeRanking(int wins, int losses, decimal powerRating)
        {
            var total = wins + losses;
            if (total == 0) return null;

            var winPct = (decimal)wins / total;
            return Math.Round(winPct * (1 + powerRating), 4);
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
            => opponentDivision?.ToUpper() == "FCS" ? 0.25 : 1.0;

        // ── Z-score dampening ─────────────────────────────────────────────────────

        public static double DampenZScore(double zScore)
        {
            if (zScore == 0) return 0;
            return Math.Sign(zScore) * Math.Log(1 + Math.Abs(zScore));
        }

        // ── Conference / team classification ──────────────────────────────────────

        // GetConferenceTier (P4/G5/Independent/Other, string-match, year-blind) was
        // removed as part of the 2026 Pac-12 reconstitution work. It had no year
        // parameter, so it could never distinguish the old (P4) Pac-12 from the new
        // (G6) one — the same structural gap that let it miss Pac-12 entirely (no
        // case for it at all, pre-existing bug independent of the 2026 change) and
        // let GetTeamHistoryAsync/GetTeamsV2Async/WeeklyRankingsService silently
        // apply a team's CURRENT conference tier to historical years. All 6 former
        // callers now use ConferenceTierService (year-aware, DB-driven via
        // TeamsConferenceHistory + Conferences) instead.

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
