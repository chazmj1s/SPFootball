namespace SaturdayPulse.Configuration
{
    /// <summary>
    /// Configuration class for all hardcoded metrics and calculation parameters.
    /// Values are loaded from appsettings.json and can be easily modified without code changes.
    /// </summary>
    public class MetricsConfiguration
    {
        /// <summary>
        /// Home field advantage in points (NCAA standard is 2.5).
        /// Applied to expected score margin when calculating Z-scores.
        /// </summary>
        public double HomeFieldAdvantage { get; set; } = 3.5;

        /// <summary>
        /// Standard number of regular season games used for normalization.
        /// </summary>
        public int StandardSeasonGames { get; set; } = 12;

        /// <summary>
        /// Multiplier for wins beyond standard season length (post-season wins).
        /// Default 0.25 means post-season wins count at 25% of regular season value.
        /// </summary>
        public double ExtraWinBump { get; set; } = 0.25;

        /// <summary>
        /// Number of years of historical data to use for projected wins calculation.
        /// </summary>
        public int ProjectedWinsHistoryYears { get; set; } = 10;

        /// <summary>
        /// Week number when SOS calculation should switch from projected to actual wins.
        /// Before this week, uses 10-year historical projections. After, uses current season wins.
        /// </summary>
        public int SosWeekThreshold { get; set; } = 6;

        /// <summary>
        /// Rounding threshold for projected wins. If decimal portion >= this value, round up.
        /// Example: 7.75 wins rounds to 8 if threshold is 0.75.
        /// </summary>
        public double ProjectedWinsRoundingThreshold { get; set; } = 0.75;

        /// <summary>
        /// Z-score threshold for "Dominant" performance classification.
        /// </summary>
        public double DominantPerformanceThreshold { get; set; } = 0.5;

        /// <summary>
        /// Z-score threshold for "Underperformed" classification (negative value).
        /// Values between this and +DominantThreshold are "Expected" performance.
        /// </summary>
        public double UnderperformedThreshold { get; set; } = -0.5;

        /// <summary>
        /// Minimum number of historical games required for a matchup to use
        /// matchup-specific variance instead of general win-based variance.
        /// Lower values = more matchups qualify, but less statistical confidence.
        /// Recommended: 10-15 games for 60 years of data.
        /// </summary>
        public int MinimumMatchupGames { get; set; } = 10;

        /// <summary>
        /// Maximum variance ratio allowed for matchup-specific adjustments.
        /// Prevents small-sample outliers from creating extreme multipliers.
        /// Example: 2.0 means matchup variance can be at most 2x the expected variance.
        /// </summary>
        public double MaxVarianceRatio { get; set; } = 2.0;

        /// <summary>
        /// ADDED — K in the experimental data-volume-weighted blending formula:
        ///   currentSeasonWeight = gamesPlayed / (InertiaConstant + gamesPlayed)
        /// Used only by RatingBlendingService / ExperimentalInertiaRatingService (the
        /// parallel comparison path against the production week-6 snapshot cliff in
        /// GamePredictionService.GetRatingsForWeekAsync). Not read by any production
        /// rating calculation yet.
        ///
        /// DEV PLACEHOLDER, not a validated constant. A "K=4 is the industry standard"
        /// claim was checked against its cited sources and didn't hold up — the source
        /// that was actually verifiable (a Towards Data Science piece on basketball/golf
        /// rating systems) never mentions K=4, ESPN FPI, or SP+. Treat 4.0 as a
        /// reasonable starting guess to tune via RatingComparisonService's backtest
        /// output, same category of unvalidated constant as ZRosterScalingConstant.
        /// </summary>
        public double InertiaConstant { get; set; } = 4.0;

        public static double[] SeedWeights = [0.50, 0.30, 0.20];
        public static double[] TrendWeights = [0.40, 0.25, 0.15, 0.12, 0.08];

    }
}
