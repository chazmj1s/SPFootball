using SaturdayPulse.Contracts;
using SaturdayPulse.Models;

namespace SaturdayPulse.Utilities
{
    /// <summary>
    /// Calibrates RatingBlendingService.ComputeSeededAnchorUnit's ZRoster/
    /// SeedRating blend weights from real games — replaces the prior
    /// hardcoded 50/50 split and arbitrary zRosterScalingConstant/stdDev=0.25
    /// compression, neither of which was ever validated against outcomes
    /// (both flagged as dev placeholders in RatingBlendingService's own
    /// class remarks). Structured as a direct sibling of TierDiscountCalculator
    /// — same shared-infrastructure shape, same single-season +
    /// standalone-or-bulk entry points — deliberately, so this fits the
    /// project's existing season-setup pattern rather than inventing a new one.
    ///
    /// ── Methodology ──────────────────────────────────────────────────────
    /// Fits HomeMargin = b0 + b1*ZRosterDiff + b2*SeedRatingDiff via ordinary
    /// least squares (closed-form normal equations, 3x3 linear system — no
    /// grid search needed here, unlike TierDiscountCalculator's D search,
    /// since both predictors enter linearly with no discount parameter to
    /// search over).
    ///
    /// Raw coefficients (b1, b2) are NOT directly comparable — ZRosterDiff and
    /// SeedRatingDiff sit on very different scales (empirically, ZRosterDiff's
    /// stddev is roughly 4x SeedRatingDiff's over the 2021-2025 data). The
    /// weights this calculator actually persists are STANDARDIZED coefficients
    /// (raw coefficient * predictor stddev / outcome stddev), normalized to
    /// sum to 1.0 — this is what answers "how much should each one matter in
    /// a blend," not the raw per-unit sensitivities.
    ///
    /// ── Window ────────────────────────────────────────────────────────────
    /// ROLLING (trailing WindowYears seasons through season-1), not growing
    /// since 1965 like TierDiscountCalculator. Confirmed with Charlie: the
    /// transfer-portal era means ZRoster's real importance is expected to
    /// drift over time in a way TrendRating/SeedRating's importance doesn't
    /// as sharply, so an all-time average would smooth away exactly the
    /// signal this calibration exists to capture. Empirically confirmed too —
    /// see AnchorBlendCoefficient remarks for the 3.98x-vs-2.98x full-history-
    /// vs-trailing-3yr finding that motivated this.
    ///
    /// ── Data source ───────────────────────────────────────────────────────
    /// Games: regular-season only, both teams FBS, from
    /// GetPlayedGamesSinceYearAsync — same source and same filters
    /// TierDiscountCalculator uses. ZRoster/SeedRating: TeamRecords for the
    /// game's own Year (bulk-loaded once per distinct year in the window, not
    /// once per game). A game is excluded (not defaulted to 0) if either
    /// team's TeamRecord for that year is missing, or has a null ZRoster or
    /// SeedRating — surfaced via GamesSkippedMissingRatingData, same
    /// transparency convention TierDiscountCalculator uses for its own
    /// skip reasons.
    /// </summary>
    public class AnchorBlendCalculator(IUnitOfWork _uow)
    {
        /// <summary>
        /// Confirmed rolling-window length. Not a magic number scattered
        /// through the file — single source, referenced by both the single-
        /// season and bulk entry points below.
        /// </summary>
        public const int DefaultWindowYears = 3;

        /// <summary>
        /// throughYear caps which games are included (Year &lt;= throughYear),
        /// same purpose as TierDiscountCalculator's identical parameter —
        /// critical for backfilling past seasons without every backfilled row
        /// pulling in games up through today.
        /// </summary>
        public async Task<AnchorBlendAnalysisResult> CalculateAsync(
            int startYear, int? throughYear = null, CancellationToken token = default)
        {
            var allGames = await _uow.Games.GetPlayedGamesSinceYearAsync(startYear, token);

            var games = allGames
                .Where(g => string.Equals(g.SeasonType, "regular", StringComparison.OrdinalIgnoreCase))
                .Where(g => g.HomeId.HasValue && g.AwayId.HasValue &&
                            g.HomePoints.HasValue && g.AwayPoints.HasValue)
                .Where(g => throughYear == null || g.Year <= throughYear.Value)
                .ToList();

            var teams = await _uow.Teams.GetAllAsync(token);
            var fbsTeamIds = teams
                .Where(t => string.Equals(t.Division, "fbs", StringComparison.OrdinalIgnoreCase))
                .Select(t => t.TeamId)
                .ToHashSet();

            games = games
                .Where(g => fbsTeamIds.Contains(g.HomeId!.Value) && fbsTeamIds.Contains(g.AwayId!.Value))
                .ToList();

            // Bulk-load TeamRecords per distinct year — avoids one query per game,
            // same convention as TierDiscountCalculator's per-year WeeklyRankings load.
            var years = games.Select(g => g.Year).Distinct().OrderBy(y => y).ToList();
            var recordsByYear = new Dictionary<int, Dictionary<int, TeamRecord>>();
            foreach (var year in years)
            {
                var yearRecords = await _uow.TeamRecords.GetByYearAsync(year, token);
                recordsByYear[year] = yearRecords.ToDictionary(r => r.TeamID);
            }

            var samples = new List<(double ZRosterDiff, double RatingDiff, double Margin)>();
            var zRosterValues = new List<double>();
            int skippedMissingRatingData = 0;

            foreach (var g in games)
            {
                if (!recordsByYear.TryGetValue(g.Year, out var yearRecords))
                {
                    skippedMissingRatingData++;
                    continue;
                }

                if (!yearRecords.TryGetValue(g.HomeId!.Value, out var homeRecord) ||
                    !yearRecords.TryGetValue(g.AwayId!.Value, out var awayRecord))
                {
                    skippedMissingRatingData++;
                    continue;
                }

                if (!homeRecord.ZRoster.HasValue || !awayRecord.ZRoster.HasValue ||
                    !homeRecord.SeedRating.HasValue || !awayRecord.SeedRating.HasValue)
                {
                    skippedMissingRatingData++;
                    continue;
                }

                var zDiff = (double)(homeRecord.ZRoster.Value - awayRecord.ZRoster.Value);
                var ratingDiff = (double)(homeRecord.SeedRating.Value - awayRecord.SeedRating.Value);
                var margin = (double)(g.HomePoints!.Value - g.AwayPoints!.Value);

                samples.Add((zDiff, ratingDiff, margin));
                zRosterValues.Add((double)homeRecord.ZRoster.Value);
                zRosterValues.Add((double)awayRecord.ZRoster.Value);
            }

            var result = new AnchorBlendAnalysisResult
            {
                StartYear = startYear,
                ComputedThroughYear = years.Count > 0 ? years.Max() : startYear,
                GamesSkippedMissingRatingData = skippedMissingRatingData
            };

            result.Fit = FitAnchorBlendModel(samples, zRosterValues);

            return result;
        }

        /// <summary>
        /// Computes weights for `season` using the trailing windowYears seasons
        /// through season-1 — never the current or later seasons. Returns null
        /// and does NOT persist if Fit.ExclusionReason is set (zero usable
        /// games in the window — expected for the first windowYears seasons
        /// of any historical backfill).
        ///
        /// UPSERT by Season — see AnchorBlendCoefficient remarks for why this is
        /// a deliberate departure from TierDiscountCalculator's append-only
        /// convention. If a row already exists for this Season, its fields are
        /// updated in place (same Id, refreshed ComputedAt) rather than a new
        /// row being inserted — rerunning a backfill is idempotent, not
        /// duplicate-generating.
        ///
        /// Intended to run in RunSeasonSetupAsync immediately after
        /// "Compute Tier Discount Coefficients", before InitializeSeasonAsync
        /// — same season-setup step ordering, new step.
        /// </summary>
        public async Task<AnchorBlendCoefficient?> ComputeAndPersistCoefficientsAsync(
            int season, int windowYears = DefaultWindowYears, CancellationToken token = default)
        {
            var startYear = season - windowYears;
            var result = await CalculateAsync(startYear, throughYear: season - 1, token: token);

            if (result.Fit.GamesUsed == 0 || result.Fit.ExclusionReason != null)
            {
                return null;
            }

            // Upsert: reuse the existing row for this Season if one exists,
            // rather than always inserting. Reuses GetLatestBySeasonAsync (the
            // same method ExperimentalInertiaRatingService already needs for
            // reads) rather than adding a second, functionally-identical
            // repository method — under upsert there's at most one row per
            // Season, so "latest for this season" and "the one row for this
            // season" are the same lookup.
            var existing = await _uow.AnchorBlendCoefficients.GetLatestBySeasonAsync(season, token);

            if (existing != null)
            {
                existing.ComputedFromStartYear = startYear;
                existing.ComputedThroughYear = result.ComputedThroughYear;
                existing.WindowYears = windowYears;
                existing.ZRosterWeight = (decimal)result.Fit.NormalizedZRosterWeight;
                existing.RatingWeight = (decimal)result.Fit.NormalizedRatingWeight;
                existing.ZRosterMean = (decimal)result.Fit.ZRosterMean;
                existing.ZRosterStdDev = (decimal)result.Fit.ZRosterStdDev;
                existing.TypicalPredictionErrorPoints = (decimal)result.Fit.RmseAtSolvedParameters;
                existing.GamesUsed = result.Fit.GamesUsed;
                existing.ComputedAt = DateTime.UtcNow;

                await _uow.SaveChangesAsync(token);
                return existing;
            }

            var coefficient = new AnchorBlendCoefficient
            {
                Season = season,
                ComputedFromStartYear = startYear,
                ComputedThroughYear = result.ComputedThroughYear,
                WindowYears = windowYears,
                ZRosterWeight = (decimal)result.Fit.NormalizedZRosterWeight,
                RatingWeight = (decimal)result.Fit.NormalizedRatingWeight,
                ZRosterMean = (decimal)result.Fit.ZRosterMean,
                ZRosterStdDev = (decimal)result.Fit.ZRosterStdDev,
                TypicalPredictionErrorPoints = (decimal)result.Fit.RmseAtSolvedParameters,
                GamesUsed = result.Fit.GamesUsed,
                ComputedAt = DateTime.UtcNow
            };

            await _uow.AnchorBlendCoefficients.AddAsync(coefficient, token);
            await _uow.SaveChangesAsync(token);

            return coefficient;
        }

        /// <summary>
        /// Runs ComputeAndPersistCoefficientsAsync for every season from
        /// startSeason through the most recent season with played FBS data (or
        /// throughSeason, if given) — for backfilling seasons that predate
        /// this feature. Mirrors TierDiscountCalculator.
        /// ComputeAndPersistCoefficientsBulkAsync exactly. Returns
        /// (Persisted, Skipped) — Skipped is seasons with zero usable games in
        /// their trailing window (expected for the first windowYears seasons
        /// of a full historical backfill, not an error).
        /// </summary>
        public async Task<(int Persisted, int Skipped)> ComputeAndPersistCoefficientsBulkAsync(
            int startSeason, int? throughSeason = null, int windowYears = DefaultWindowYears,
            CancellationToken token = default)
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
                var result = await ComputeAndPersistCoefficientsAsync(season, windowYears, token);
                if (result == null) skipped++; else persisted++;
            }

            return (persisted, skipped);
        }

        // ══════════════════════════════════════════════════════════════════
        // Regression fit
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Fits Margin = b0 + b1*ZRosterDiff + b2*RatingDiff via OLS (closed-
        /// form normal equations, 3x3 system solved via Cramer's rule — no
        /// search needed, both predictors are linear with no parameter like
        /// TierDiscountCalculator's D to search over). Converts b1/b2 to
        /// standardized coefficients (coefficient * predictor stddev / outcome
        /// stddev) before persisting anything, since raw b1/b2 aren't
        /// comparable across two predictors on very different scales — see
        /// class remarks.
        /// </summary>
        private static AnchorBlendFitResult FitAnchorBlendModel(
            List<(double ZRosterDiff, double RatingDiff, double Margin)> samples,
            List<double> zRosterValues)
        {
            var fit = new AnchorBlendFitResult { GamesUsed = samples.Count };

            if (samples.Count < 10)
            {
                fit.ExclusionReason = samples.Count == 0
                    ? "No usable games available"
                    : $"Only {samples.Count} usable games — too few to fit reliably (minimum 10)";
                return fit;
            }

            int n = samples.Count;
            double sumX1 = 0, sumX2 = 0, sumY = 0;
            double sumX1X1 = 0, sumX2X2 = 0, sumX1X2 = 0, sumX1Y = 0, sumX2Y = 0;

            foreach (var s in samples)
            {
                sumX1 += s.ZRosterDiff;
                sumX2 += s.RatingDiff;
                sumY += s.Margin;
                sumX1X1 += s.ZRosterDiff * s.ZRosterDiff;
                sumX2X2 += s.RatingDiff * s.RatingDiff;
                sumX1X2 += s.ZRosterDiff * s.RatingDiff;
                sumX1Y += s.ZRosterDiff * s.Margin;
                sumX2Y += s.RatingDiff * s.Margin;
            }

            // Normal equations, 3x3 system:
            //   [ n     sumX1   sumX2  ] [b0]   [sumY  ]
            //   [ sumX1 sumX1X1 sumX1X2] [b1] = [sumX1Y]
            //   [ sumX2 sumX1X2 sumX2X2] [b2]   [sumX2Y]
            var a = new double[,]
            {
                { n,     sumX1,    sumX2    },
                { sumX1, sumX1X1,  sumX1X2  },
                { sumX2, sumX1X2,  sumX2X2  }
            };
            var rhs = new[] { sumY, sumX1Y, sumX2Y };

            var det = Determinant3x3(a);
            if (Math.Abs(det) < 1e-9)
            {
                // Degenerate — e.g. ZRosterDiff and RatingDiff perfectly
                // collinear across every game in this window (not expected in
                // practice with real data, but guarded rather than dividing
                // by ~zero).
                fit.ExclusionReason = "Degenerate fit — ZRosterDiff and RatingDiff were not independently informative in this window";
                return fit;
            }

            var b0 = Determinant3x3(ReplaceColumn(a, 0, rhs)) / det;
            var b1 = Determinant3x3(ReplaceColumn(a, 1, rhs)) / det;
            var b2 = Determinant3x3(ReplaceColumn(a, 2, rhs)) / det;

            // Residuals / RMSE
            double sse = 0;
            foreach (var s in samples)
            {
                var predicted = b0 + b1 * s.ZRosterDiff + b2 * s.RatingDiff;
                var err = s.Margin - predicted;
                sse += err * err;
            }
            var rmse = Math.Sqrt(sse / n);

            // Standardized coefficients: coef * predictor_stddev / outcome_stddev.
            double x1Mean = sumX1 / n, x2Mean = sumX2 / n, yMean = sumY / n;
            double x1Var = 0, x2Var = 0, yVar = 0;
            foreach (var s in samples)
            {
                x1Var += (s.ZRosterDiff - x1Mean) * (s.ZRosterDiff - x1Mean);
                x2Var += (s.RatingDiff - x2Mean) * (s.RatingDiff - x2Mean);
                yVar += (s.Margin - yMean) * (s.Margin - yMean);
            }
            var x1Std = Math.Sqrt(x1Var / n);
            var x2Std = Math.Sqrt(x2Var / n);
            var yStd = Math.Sqrt(yVar / n);

            var std1 = yStd > 1e-9 ? b1 * x1Std / yStd : 0.0;
            var std2 = yStd > 1e-9 ? b2 * x2Std / yStd : 0.0;

            // Normalize to weights summing to 1.0. Guard against both
            // standardized coefficients being ~0 (would mean neither predictor
            // carries any real signal in this window — not expected, but
            // falling back to an even split rather than a divide-by-zero if
            // it ever happens).
            var absSum = Math.Abs(std1) + Math.Abs(std2);
            double normZRoster, normRating;
            if (absSum < 1e-9)
            {
                normZRoster = 0.5;
                normRating = 0.5;
            }
            else
            {
                normZRoster = Math.Abs(std1) / absSum;
                normRating = Math.Abs(std2) / absSum;
            }

            fit.RawZRosterCoefficient = Math.Round(b1, 4);
            fit.RawRatingCoefficient = Math.Round(b2, 4);
            fit.StandardizedZRosterCoefficient = Math.Round(std1, 4);
            fit.StandardizedRatingCoefficient = Math.Round(std2, 4);
            fit.NormalizedZRosterWeight = Math.Round(normZRoster, 4);
            fit.NormalizedRatingWeight = Math.Round(normRating, 4);
            fit.RmseAtSolvedParameters = Math.Round(rmse, 4);

            // Real ZRoster distribution for this window — replaces the old
            // hardcoded mean=0/stdDev=0.25 compression in ComputeSeededAnchorUnit.
            if (zRosterValues.Count > 0)
            {
                var zMean = zRosterValues.Average();
                var zVar = zRosterValues.Sum(v => (v - zMean) * (v - zMean)) / zRosterValues.Count;
                fit.ZRosterMean = Math.Round(zMean, 4);
                fit.ZRosterStdDev = Math.Round(Math.Sqrt(zVar), 4);
            }

            return fit;
        }

        private static double Determinant3x3(double[,] m) =>
            m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) -
            m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) +
            m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);

        private static double[,] ReplaceColumn(double[,] m, int col, double[] values)
        {
            var result = (double[,])m.Clone();
            for (var row = 0; row < 3; row++)
                result[row, col] = values[row];
            return result;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // DTOs
    // ══════════════════════════════════════════════════════════════════════

    public class AnchorBlendFitResult
    {
        public double RawZRosterCoefficient { get; set; }
        public double RawRatingCoefficient { get; set; }

        /// <summary>Standardized (comparable) coefficients — what actually
        /// determines the persisted weights. See class remarks.</summary>
        public double StandardizedZRosterCoefficient { get; set; }
        public double StandardizedRatingCoefficient { get; set; }

        /// <summary>Sums to 1.0 with NormalizedRatingWeight — these two are
        /// what ComputeSeededAnchorUnit actually consumes.</summary>
        public double NormalizedZRosterWeight { get; set; }
        public double NormalizedRatingWeight { get; set; }

        public double ZRosterMean { get; set; }
        public double ZRosterStdDev { get; set; }

        public double RmseAtSolvedParameters { get; set; }
        public int GamesUsed { get; set; }
        public string? ExclusionReason { get; set; }
    }

    public class AnchorBlendAnalysisResult
    {
        public int StartYear { get; set; }
        public int ComputedThroughYear { get; set; }
        public AnchorBlendFitResult Fit { get; set; } = new();
        public int GamesSkippedMissingRatingData { get; set; }
    }
}
