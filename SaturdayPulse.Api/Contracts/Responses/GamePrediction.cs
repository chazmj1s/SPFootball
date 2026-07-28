namespace SaturdayPulse.Contracts.Responses
{
    /// <summary>
    /// Represents a predicted game outcome.
    /// </summary>
    public class GamePrediction
    {
        public int     GameId                 { get; set; }
        public int     Week                   { get; set; }
        public string  TeamName               { get; set; } = "";
        public string  OpponentName           { get; set; } = "";
        public char    Location               { get; set; }
        public int     TeamWins               { get; set; }
        public int     OpponentWins           { get; set; }
        public double  PredictedTeamScore     { get; set; }
        public double  PredictedOpponentScore { get; set; }
        public double  ExpectedMargin         { get; set; }
        public double  MarginOfError          { get; set; }

        /// <summary>
        /// The real, interpolated historical standard deviation for this matchup's
        /// strength differential (from AvgScoreDifferential), adjusted only for rivalry
        /// variance where applicable. No floor or cap is applied — a prior version
        /// clamped this to [7, 21] using a constant borrowed from the legacy, retired
        /// AvgScoreDelta class, which compressed genuine historical volatility toward a
        /// guessed range. MarginOfError below is this same value, rounded for display.
        /// </summary>
        public double   RawStdDev             { get; set; }
        public string?  Confidence            { get; set; }

        /// <summary>
        /// Sandbox-only footer text explaining MarginOfError/Confidence in plain
        /// language, specific to this matchup. Null for real, calendar-anchored
        /// predictions (PredictMatchup/PredictMatchups/PredictMatchupsWithRatings) —
        /// deliberately not built for those yet; see GamePredictionService remarks.
        /// Never names a rivalry even when the pairing is one of the 52 curated ones,
        /// since Sandbox matchups are hypothetical and can pair any two team-seasons —
        /// a curated pair's real historical data still shapes the underlying numbers,
        /// the text just describes the effect generically ("matchups between these
        /// two") rather than naming the rivalry.
        /// </summary>
        public string?  ConfidenceExplanation { get; set; }
        public string?  RivalryNote           { get; set; }
        public decimal? TeamPowerRating       { get; set; }
        public decimal? OpponentPowerRating   { get; set; }

        public string LocationDisplay => Location switch
        {
            'H' => "vs",
            'A' => "@",
            'N' => "N",
            _   => ""
        };

        /// <summary>
        /// Win probability for TeamName (0.0–1.0).
        /// ExpectedMargin > 0 → favored → WinProbability > 0.50.
        /// </summary>
        public double WinProbability
        {
            get
            {
                // Numerical safety floor only — prevents divide-by-zero if a bucket's
                // real historical stddev were ever exactly 0. This is NOT a business/
                // calibration constant (the previous version floored with
                // AvgScoreDelta.DefaultAverageScoreDelta, a margin default, not a
                // stddev value, borrowed from a class that's otherwise retired).
                var sigma = Math.Max(RawStdDev, 0.01);
                return NormalCdf(ExpectedMargin / sigma);
            }
        }

        public double OpponentWinProbability        => 1.0 - WinProbability;
        public string WinProbabilityDisplay         => $"{WinProbability:P0}";
        public string OpponentWinProbabilityDisplay => $"{OpponentWinProbability:P0}";

        public string PredictionSummary =>
            $"{TeamName} {PredictedTeamScore:F1} {LocationDisplay} {OpponentName} {PredictedOpponentScore:F1} " +
            $"(±{MarginOfError:F1}, {Confidence} confidence, {WinProbabilityDisplay})";

        // Abramowitz & Stegun approximation (26.2.17) — accurate to ~7 decimal places
        private static double NormalCdf(double z)
        {
            const double p  =  0.2316419;
            const double b1 =  0.319381530;
            const double b2 = -0.356563782;
            const double b3 =  1.781477937;
            const double b4 = -1.821255978;
            const double b5 =  1.330274429;

            bool negative = z < 0;
            z = Math.Abs(z);

            double t    = 1.0 / (1.0 + p * z);
            double poly = t * (b1 + t * (b2 + t * (b3 + t * (b4 + t * b5))));
            double pdf  = Math.Exp(-0.5 * z * z) / Math.Sqrt(2 * Math.PI);
            double cdf  = 1.0 - pdf * poly;

            return negative ? 1.0 - cdf : cdf;
        }
    }
}
