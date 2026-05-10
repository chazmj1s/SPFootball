using NCAA_Power_Ratings.Contracts.Requests;

namespace NCAA_Power_Ratings.Contracts.Requests
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
