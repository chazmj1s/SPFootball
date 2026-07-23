namespace SaturdayPulse.Contracts.Responses
{
    /// <summary>
    /// Combined view of UserProfile + UserContactInfo for GET /api/user/me.
    /// ExpiryDate/IsEntitled reflect the active CFB Season Pass row in
    /// UserEntitlement — set by UserProfileService.ToResponse, not computed here.
    /// UserProfile.ExpiryDate remains in the schema but is no longer this
    /// response's source of truth for entitlement.
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
    }
}
