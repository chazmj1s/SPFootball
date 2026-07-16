namespace SaturdayPulse.Contracts.Responses
{
    /// <summary>
    /// Combined view of UserProfile + UserContactInfo for GET /api/user/me.
    /// Never exposes ExpiryDate directly as an editable field — read-only,
    /// set only by the entitlement/payment sync process.
    /// </summary>
    public class UserProfileResponse
    {
        public string UserId { get; set; } = null!;
        public string Handle { get; set; } = null!;
        public int? PrimaryTeamId { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public bool IsEntitled => ExpiryDate.HasValue && ExpiryDate.Value > DateTime.UtcNow;

        public string Email { get; set; } = null!;
        public bool EmailVerified { get; set; }
        public string? PhoneNumber { get; set; }
        public bool PhoneVerified { get; set; }
        public bool MarketingSmsConsent { get; set; }
    }
}
