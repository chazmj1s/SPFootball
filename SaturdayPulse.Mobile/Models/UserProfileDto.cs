namespace SaturdayPulse.Models
{
    /// <summary>
    /// Mirrors SaturdayPulse.Api's UserProfileResponse field-for-field.
    ///
    /// Cleaned up 2026-07-26 against the real UserProfileResponse.cs (rather
    /// than the prior guesswork): removed HandleChangedAt/CreatedAt/UpdatedAt/
    /// IsSynced, none of which exist on the actual response - they were
    /// silently deserializing to null/default the whole time. Added
    /// EmailVerified/PhoneVerified, which DO exist server-side but were
    /// missing here. MarketingSmsConsent is now a plain bool, not bool? -
    /// matches the server's non-nullable field; ApplyProfile no longer needs
    /// a `?? false`.
    /// </summary>
    public class UserProfileDto
    {
        public string UserId { get; set; } = string.Empty;
        public string Handle { get; set; } = string.Empty;
        public int? PrimaryTeamId { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public bool IsEntitled { get; set; }
        public bool IsAdmin { get; set; }

        public string Email { get; set; } = string.Empty;
        public bool EmailVerified { get; set; }
        public string? PhoneNumber { get; set; }
        public bool PhoneVerified { get; set; }
        public bool MarketingSmsConsent { get; set; }
        public bool MarketingEmailConsent { get; set; }

        /// <summary>
        /// Full entitlement history - any product, any status, not just the
        /// currently-active one. Backs the new Season Pass panel's "current
        /// and available" list, mirroring how the admin console's Users page
        /// shows every grant a user has ever held.
        /// </summary>
        public List<EntitlementSummaryDto> Entitlements { get; set; } = new();
    }

    /// <summary>Mirrors SaturdayPulse.Api's EntitlementSummary.</summary>
    public class EntitlementSummaryDto
    {
        public string ProductKey { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty; // "stripe" | "manual-grant" | "beta"
        public DateTime? ExpiryDate { get; set; }
        public int? PassYear { get; set; }
        public bool IsActive { get; set; }

        /// <summary>
        /// Get-only, so System.Text.Json ignores it entirely during
        /// deserialization (no matching JSON field to assign) - safe to add
        /// without touching the wire contract. Exists purely so the Season
        /// Pass panel's XAML doesn't need a MultiBinding/converter for what's
        /// really just "Source · expires DATE".
        /// </summary>
        public string DisplayLine => ExpiryDate is { } exp
            ? $"{Source} \u00b7 expires {exp:MMM d, yyyy}"
            : Source;
    }

    public class FollowedGamePairDto
    {
        public int Team1Id { get; set; }
        public int Team2Id { get; set; }
    }
}
