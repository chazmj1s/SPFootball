using System.ComponentModel.DataAnnotations;

namespace SaturdayPulse.Models
{
    /// <summary>
    /// One row per user. UserId is the immutable identity (local GUID today,
    /// will become the Auth0 `sub` claim once real auth lands — every other
    /// table FKs to UserId, never to Handle).
    /// Handle is the user-editable, still-unique display identifier.
    /// ExpiryDate is the single field driving entitlement — only the
    /// sync/payment process is allowed to write it; the app only reads it.
    /// </summary>
    public class UserProfile
    {
        [Key]
        [MaxLength(64)]
        public string UserId { get; set; } = null!;

        [Required]
        [MaxLength(20)]
        public string Handle { get; set; } = null!;

        public DateTime? HandleChangedAt { get; set; }

        public int? PrimaryTeamId { get; set; }

        // Entitlement — set only by the external payment/sync mechanism.
        public DateTime? ExpiryDate { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsSynced { get; set; }
        public bool IsAdmin { get; set; }

    }
}
