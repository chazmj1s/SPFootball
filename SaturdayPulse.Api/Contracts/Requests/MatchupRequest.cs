namespace SaturdayPulse.Contracts.Requests
{
    /// <summary>
    /// Request object for a single matchup prediction.
    /// </summary>
    public class MatchupRequest
    {
        public string TeamName     { get; set; } = "";
        public string OpponentName { get; set; } = "";
        public char   Location     { get; set; } // 'H' = home, 'A' = away, 'N' = neutral
        public int    Week         { get; set; }
    }
}
