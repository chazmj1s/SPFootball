namespace SaturdayPulse.Models
{
    // Extracted out of UserApiService.cs (2026-07-22) per its own long-standing
    // comment that it should live with the rest of the DTOs. No field changes
    // from where it lived before.
    public class UserProfileDto
    {
        public string UserId { get; set; } = string.Empty;
        public string Handle { get; set; } = string.Empty;
        public DateTime? HandleChangedAt { get; set; }
        public int? PrimaryTeamId { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsSynced { get; set; }
        public bool IsEntitled { get; set; }
        public bool IsAdmin { get; set; }

        // ASSUMPTION: GetMe() merges UserContactInfo into the same response
        // (the controller's class summary is "Profile, contact info, and
        // follow management"). If it doesn't, these just deserialize as
        // null/false and Email/Phone show blank in Settings until a
        // dedicated contact-info fetch is added — confirm against
        // UserProfileService.cs when you get a chance.
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public bool? MarketingSmsConsent { get; set; }
    }

    public class FollowedGamePairDto
    {
        public int Team1Id { get; set; }
        public int Team2Id { get; set; }
    }
}
