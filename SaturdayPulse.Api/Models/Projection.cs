namespace SaturdayPulse.Models
{
    /// <summary>
    /// Persisted game projection snapshot.
    /// One row per (GameId, Year, Week) — history is kept across weekly uploads,
    /// mirroring the WeeklyRankings pattern.
    /// </summary>
    public class Projection
    {
        public int     ProjectionId       { get; set; }
        public int     GameId             { get; set; }
        public int     Year               { get; set; }
        public int     Week               { get; set; }
        public int     HomeTeamId         { get; set; }
        public int     AwayTeamId         { get; set; }

        public int     HomePoints         { get; set; }
        public int     AwayPoints         { get; set; }

        /// <summary>
        /// Positive = home team favored. Rounded to the nearest tenth —
        /// matches the precision of the real Vegas lines feed elsewhere in the
        /// app (VegasLines/Lines), not a half-point "avoid a push" convention.
        /// Deliberately NOT derived from HomePoints - AwayPoints — that
        /// difference of two already-rounded integers can only ever land on a
        /// whole number (the original bug: every spread displaying as X.0).
        /// Computed instead from the raw, continuous predicted differential in
        /// GamePredictionService.BuildProjection. As a result, PredictedSpread
        /// and HomePoints - AwayPoints won't always exactly agree — that's
        /// expected, not a bug.
        /// </summary>
        public decimal PredictedSpread    { get; set; }

        /// <summary>Derived from HomePoints + AwayPoints — O/U is fine as a whole number, unlike spread.</summary>
        public decimal PredictedTotal     { get; set; }

        /// <summary>0.0 – 1.0</summary>
        public decimal HomeWinProbability { get; set; }
    }
}
