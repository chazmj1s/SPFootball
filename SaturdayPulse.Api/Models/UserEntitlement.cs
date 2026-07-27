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
    ///
    /// PassYear (added 2026-07-26): the season this grant covers, as an explicit
    /// value rather than something inferred from ExpiryDate. Needed for Apple/
    /// Google IAP reconciliation, where a purchased subscription product maps to
    /// a specific season (e.g. a "2026 CFB Season Pass" SKU) and the app needs to
    /// know which season that is directly, not by parsing a date convention.
    /// Nullable - rows created before this field existed, and non-seasonal grants
    /// like the admin dev-toggle's "forever" sentinel, legitimately have no season.
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

        public int? PassYear { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
