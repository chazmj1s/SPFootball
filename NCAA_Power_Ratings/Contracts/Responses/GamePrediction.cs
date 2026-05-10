namespace NCAA_Power_Ratings.Contracts.Responses
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
        /// Unclamped std dev used for win probability math.
        /// Distinct from MarginOfError which is capped at [7, 21] for display.
        /// </summary>
        public double   RawStdDev             { get; set; }
        public string?  Confidence            { get; set; }
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
                var sigma = Math.Max(RawStdDev, 7.0);
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
