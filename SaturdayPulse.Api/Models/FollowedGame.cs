namespace SaturdayPulse.Models
{
    /// <summary>
    /// Represents following a matchup/rivalry (a team pair), not one specific
    /// game instance — survives across seasons the same way PersonalGameService's
    /// team-pair key does. Team1Id/Team2Id are always stored canonically
    /// ordered (low, high) so 251:201 and 201:251 are the same row — enforced
    /// at write time in the repository, not by the DB itself.
    /// </summary>
    public class FollowedGame
    {
        public string UserId { get; set; } = null!;
        public int Team1Id { get; set; } // always the lower TeamId
        public int Team2Id { get; set; } // always the higher TeamId
        public DateTime FollowedAt { get; set; }
        public bool IsSynced { get; set; }
    }
}
