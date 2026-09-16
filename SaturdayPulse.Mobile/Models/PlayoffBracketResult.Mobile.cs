namespace SaturdayPulse.Models
{
    /// <summary>
    /// Mirrors the Api-side PlayoffBracketResult field-for-field so
    /// HttpClient.GetFromJsonAsync deserializes it directly (same convention
    /// as PlayoffFieldResult/PlayoffSeed).
    /// </summary>
    public class PlayoffBracketResult
    {
        public int Year { get; set; }
        public int Week { get; set; }

        /// <summary>In order: First Round, Quarterfinals, Semifinals, National Championship.</summary>
        public List<BracketRoundResult> Rounds { get; set; } = new();

        public List<string> Log { get; set; } = new();
    }

    public class BracketRoundResult
    {
        public string RoundLabel { get; set; } = "";
        public List<BracketMatchupResult> Matchups { get; set; } = new();
    }

    /// <summary>
    /// One matchup within a round. Team1/Team2 rather than Home/Away — only
    /// First Round is actually campus-sited (higher seed hosts); Quarterfinals
    /// on, it's neutral, so "home" isn't meaningful past Round 1.
    /// </summary>
    public class BracketMatchupResult
    {
        public int    Team1Seed      { get; set; }
        public string Team1Name      { get; set; } = "";
        public double Team1ProjScore { get; set; }

        public int    Team2Seed      { get; set; }
        public string Team2Name      { get; set; } = "";
        public double Team2ProjScore { get; set; }

        public int    WinnerSeed     { get; set; }
        public string WinnerName     { get; set; } = "";

        /// <summary>Set client-side by position within its round, not part of
        /// the wire contract — same convention as GameResult.IsOddRow.</summary>
        public bool IsOddRow { get; set; }

        // ── Display helpers ─────────────────────────────────────────────
        public string DisplayTeam1Score => $"({Math.Round(Team1ProjScore):0})";
        public string DisplayTeam2Score => $"({Math.Round(Team2ProjScore):0})";
        public bool   Team1Wins         => WinnerSeed == Team1Seed;
        public bool   Team2Wins         => WinnerSeed == Team2Seed;
    }
}
