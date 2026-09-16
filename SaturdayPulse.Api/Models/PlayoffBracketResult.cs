namespace SaturdayPulse.Models
{
    /// <summary>
    /// Result of GET /api/productiongamedata/playoff-bracket — the full
    /// projected 12-team CFP bracket, First Round through the National
    /// Championship, computed in order off PlayoffSeedingService's field
    /// and the matchup engine. No toggle, no partial state — every round is
    /// computed and returned in one call (Charlie, 2026-09-15: "we're
    /// projecting all these games, so there's no reason to wait").
    /// </summary>
    public class PlayoffBracketResult
    {
        public int Year { get; set; }
        public int Week { get; set; }

        /// <summary>In order: First Round, Quarterfinals, Semifinals, National Championship.</summary>
        public List<BracketRoundResult> Rounds { get; set; } = new();

        /// <summary>Selection/computation trace — debugging/QA, not for display.</summary>
        public List<string> Log { get; set; } = new();
    }

    public class BracketRoundResult
    {
        public string RoundLabel { get; set; } = "";
        public List<BracketMatchupResult> Matchups { get; set; } = new();
    }

    /// <summary>
    /// One matchup within a round. Team1/Team2 rather than Home/Away — only
    /// First Round is actually played at a campus site (higher seed hosts);
    /// Quarterfinals on, the game is neutral-site, so "home" isn't meaningful
    /// past Round 1. Seed numbers are the real bracket seed for that team,
    /// carried through from whichever earlier round they won.
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
    }
}
