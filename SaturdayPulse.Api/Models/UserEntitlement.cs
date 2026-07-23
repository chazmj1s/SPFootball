using System.ComponentModel.DataAnnotations;

namespace SaturdayPulse.Models
{
    /// <summary>
    /// One row per (user, product) entitlement grant. Scoped to a single product
    /// today — "cfb-season-pass" — via UserEntitlementRepository's hardcoded key,
    /// not by any logic in this model. ProductKey stays on the schema so a future
    /// league doesn't require a migration, but nothing currently resolves between
    /// multiple products; that's deferred until a second league actually exists
    /// (see session-handoff notes — bringing other leagues online is a major
    /// rework, not just a new ProductKey value).
    /// Only the payment/sync process or an admin grant should write ExpiryDate.
    /// </summary>
    public class UserEntitlement
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(64)]
        public string UserId { get; set; } = null!;

        [Required, MaxLength(64)]
        public string ProductKey { get; set; } = null!; // "cfb-season-pass" — the only value in use today

        public DateTime? ExpiryDate { get; set; }

        [Required, MaxLength(32)]
        public string Source { get; set; } = null!; // "stripe" | "manual-grant" | "beta"

        public DateTime CreatedAt { get; set; }
    }
}
