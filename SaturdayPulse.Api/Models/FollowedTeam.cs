namespace SaturdayPulse.Models
{
    /// <summary>
    /// A follow either exists or doesn't per user/team pair — composite key,
    /// no surrogate id. IsSynced/UpdatedAt-less by design: a row's mere
    /// existence (or deletion) IS the change, so a future sync job just
    /// diffs which rows exist locally vs. server-side rather than tracking
    /// per-row dirty state.
    /// </summary>
    public class FollowedTeam
    {
        public string UserId { get; set; } = null!;
        public int TeamId { get; set; }
        public DateTime FollowedAt { get; set; }
        public bool IsSynced { get; set; }
    }
}
