namespace SaturdayPulse.Contracts.Responses
{
    /// <summary>
    /// One row in the admin console's Users page - profile summary plus every
    /// entitlement the user has (any product, any status), so the console can
    /// show what they're currently granted without a second round-trip.
    /// </summary>
    public class AdminUserSummaryResponse
    {
        public string UserId { get; set; } = null!;
        public string Handle { get; set; } = null!;
        public string? Email { get; set; }
        public bool IsAdmin { get; set; }
        public List<AdminEntitlementResponse> Entitlements { get; set; } = new();
    }

    public class AdminEntitlementResponse
    {
        public string ProductKey { get; set; } = null!;
        public string Source { get; set; } = null!; // "stripe" | "manual-grant" | "beta"
        public DateTime? ExpiryDate { get; set; }
        public int? PassYear { get; set; }
        public bool IsActive { get; set; }
    }
}
