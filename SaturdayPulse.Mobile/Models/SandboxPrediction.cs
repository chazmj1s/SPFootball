namespace SaturdayPulse.Models
{
    /// <summary>
    /// Response model for the sandbox/predict endpoint.
    /// Carries the projected score and metadata for a cross-year matchup.
    /// </summary>
    public class SandboxPrediction
    {
        public string TeamName           { get; set; } = string.Empty;
        public int    TeamYear           { get; set; }
        public string OpponentName       { get; set; } = string.Empty;
        public int    OpponentYear       { get; set; }
        public double PredictedTeamScore { get; set; }
        public double PredictedOppScore  { get; set; }
        public double ExpectedMargin     { get; set; }
        public double MarginOfError      { get; set; }
        public string Confidence         { get; set; } = string.Empty;
        public string? RivalryNote       { get; set; }
        public string? Summary           { get; set; }

        // ── Display helpers ───────────────────────────────────────────────
        public string TeamScore => $"{(int)Math.Round(PredictedTeamScore)}";
        public string OppScore  => $"{(int)Math.Round(PredictedOppScore)}";
        public string MarginDisplay => ExpectedMargin >= 0
            ? $"{TeamName} by {Math.Abs(ExpectedMargin):F1}"
            : $"{OpponentName} by {Math.Abs(ExpectedMargin):F1}";
    }
}
