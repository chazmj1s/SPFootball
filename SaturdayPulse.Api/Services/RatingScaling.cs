namespace SaturdayPulse.Services
{
    /// <summary>
    /// Shared z-score / [0,1] unit-scale mapping, used anywhere a raw PowerRating-style
    /// value needs to be compared or blended against another value on a different scale
    /// (rolling averages, ZRoster, live in-season data). Centralizes what was previously
    /// a private helper duplicated in RollingAverageService (NormalizePowerRating /
    /// ToUnitScale) — that method can optionally be changed to delegate here, but this
    /// file does not modify RollingAverageService itself.
    /// </summary>
    public static class RatingScaling
    {
        /// <summary>
        /// Z-score, clamp to +-clampSigma std devs (default 2.0), map onto [0,1]
        /// centered at 0.5 for the default clamp. The mapping itself (0.5 + z/4.0)
        /// never changes — only how far z is allowed to go before it clamps — so a
        /// raw value already inside +-2 std devs maps to the exact same unit value
        /// regardless of clampSigma; only previously-clamped extremes change.
        ///
        /// UPDATED 2026-09-03 (SeedRating dispersion fix). clampSigma added so
        /// RollingAverageService can widen the clamp specifically for SeedRating
        /// (consumed by RatingBlendingService.ComputeSeededAnchorUnit as the
        /// prediction engine's anchor, where the old +-2 std dev clamp pooled most
        /// elite teams near the same ceiling, compressing preseason/early-season
        /// predictions) without touching TrendRating/PedigreeRating's clamp — those
        /// stay +-2 std devs (the default) since they feed the Rankings page's
        /// Trend/Pedigree graph, which needs the [0,1] bound to render. A wider
        /// clamp means the output is no longer guaranteed to land in [0,1] — by
        /// design here; see RollingAverageService.ComputeSeed remarks.
        /// </summary>
        public static double ToUnitScale(double rawValue, double mean, double stdDev, double clampSigma = 2.0)
        {
            if (stdDev <= 0) return 0.5;
            var z = (rawValue - mean) / stdDev;
            var clamped = Math.Max(-clampSigma, Math.Min(clampSigma, z));
            return 0.5 + (clamped / 4.0);
        }

        /// <summary>
        /// Inverse of ToUnitScale — maps a [0,1] blended value back into raw PowerRating
        /// point terms for a given distribution. Needed because K=4 blending happens on
        /// the unit scale, but downstream consumers (CalculatePrediction, WeeklyRankings)
        /// expect real PowerRating values.
        /// </summary>
        public static double FromUnitScale(double unitValue, double mean, double stdDev)
        {
            var clamped = (unitValue - 0.5) * 4.0;   // undo the 0.5 + z/4 mapping
            return mean + (clamped * stdDev);
        }
    }
}
