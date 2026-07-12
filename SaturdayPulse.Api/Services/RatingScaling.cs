namespace SaturdayPulse.Services
{
    /// <summary>
    /// Shared z-score / [0,1] unit-scale mapping, used anywhere a raw PowerRating-style
    /// value needs to be compared or blended against another value on a different scale
    /// (rolling averages, ZRoster, live in-season data). Centralizes what was previously
    /// a private helper duplicated in RollingAverageService (NormalizePowerRating /
    /// ToUnitScale) — that method can optionally be changed to delegate here, but this
    /// file does not modify RollingAverageService itself.
    ///
    /// NEW FILE — part of the K=4 inertia-blending experimental comparison path.
    /// Not referenced by any production rating path.
    /// </summary>
    public static class RatingScaling
    {
        /// <summary>Z-score, clamp to +-2 std devs, map onto [0,1] centered at 0.5.</summary>
        public static double ToUnitScale(double rawValue, double mean, double stdDev)
        {
            if (stdDev <= 0) return 0.5;
            var z = (rawValue - mean) / stdDev;
            var clamped = Math.Max(-2.0, Math.Min(2.0, z));
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
