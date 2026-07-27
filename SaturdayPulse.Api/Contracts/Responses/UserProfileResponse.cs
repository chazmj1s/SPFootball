namespace SaturdayPulse.Contracts.Responses
{
    /// <summary>
    /// Combined view of UserProfile + UserContactInfo for GET /api/user/me.
    /// ExpiryDate/IsEntitled reflect the active CFB Season Pass row in
    /// UserEntitlement — set by UserProfileService.ToResponse, not computed here.
    /// UserProfile.ExpiryDate remains in the schema but is no longer this
    /// response's source of truth for entitlement.
    ///
    /// Entitlements (added for the mobile Season Pass panel) is the full
    /// history, not just the currently-active row - lets the app show
    /// "current and available" the same way the admin console's Users page
    /// does, without a second endpoint.
    /// </summary>
    public class UserProfileResponse
    {
        public string UserId { get; set; } = null!;
        public string Handle { get; set; } = null!;
        public int? PrimaryTeamId { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public bool IsEntitled { get; set; }
        public bool IsAdmin { get; set; }
        public string Email { get; set; } = null!;
        public bool EmailVerified { get; set; }
        public string? PhoneNumber { get; set; }
        public bool PhoneVerified { get; set; }
        public bool MarketingSmsConsent { get; set; }
        public bool MarketingEmailConsent { get; set; }
        public List<EntitlementSummary> Entitlements { get; set; } = new();
    }

    public class EntitlementSummary
    {
        public string ProductKey { get; set; } = null!;
        public string Source { get; set; } = null!; // "stripe" | "manual-grant" | "beta"
        public DateTime? ExpiryDate { get; set; }
        public int? PassYear { get; set; }
        public bool IsActive { get; set; }
    }
}
