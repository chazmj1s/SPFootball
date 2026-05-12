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
        public double HomeFieldAdvantage { get; set; } = 2.5;

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
    }
}
