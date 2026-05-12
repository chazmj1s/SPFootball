namespace SaturdayPulse.Models
{
    /// <summary>
    /// Filter options for team rankings
    /// </summary>
    public enum RankingFilter
    {
        All,
        Top25,
        Conference,
        Division,
        P4,         // Power 4 conferences only
        G5,         // Group of 5 conferences only
        Independent // Independent teams
    }

    /// <summary>
    /// Sort options for team rankings
    /// </summary>
    public enum RankingSort
    {
        Rank,           // Overall rank
        TeamName,
        PowerRating,
        Record,
        Conference,
        SOS,
        TierRank,       // Rank within tier (P4/G5)
        Tier            // Group by tier
    }
}
