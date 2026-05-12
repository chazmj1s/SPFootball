using SaturdayPulse.Contracts.Requests;

namespace SaturdayPulse.Contracts.Requests
{
    /// <summary>
    /// Request model for batch matchup predictions.
    /// </summary>
    public class MatchupBatchRequest
    {
        public int               Year     { get; set; }
        public List<MatchupRequest> Matchups { get; set; } = new();
    }
}
