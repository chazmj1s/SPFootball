using System.ComponentModel.DataAnnotations;

namespace SaturdayPulse.Models
{
    /// <summary>
    /// PII split out from UserProfile so it stays a small, auditable surface
    /// (easy to answer a "delete my data" request from one table). Email and
    /// SMS consent are tracked separately with their own timestamps — account
    /// email is implied by having an account, but marketing/notification
    /// email and any SMS at all need their own explicit opt-in trail.
    /// </summary>
    public class UserContactInfo
    {
        [Key]
        [MaxLength(64)]
        public string UserId { get; set; } = null!;

        [Required]
        [MaxLength(256)]
        public string Email { get; set; } = null!;
        public DateTime? EmailVerifiedAt { get; set; }

        [MaxLength(20)]
        public string? PhoneNumber { get; set; } // E.164 format, e.g. +15125551234
        public DateTime? PhoneVerifiedAt { get; set; }

        public bool MarketingEmailConsent { get; set; }
        public DateTime? MarketingEmailConsentAt { get; set; }

        // TCPA-relevant — keep a real audit trail on this one.
        public bool MarketingSmsConsent { get; set; }
        public DateTime? MarketingSmsConsentAt { get; set; }
        [MaxLength(64)]
        public string? MarketingSmsConsentSource { get; set; } // e.g. "signup_checkbox_v1"

        public DateTime UpdatedAt { get; set; }
        public bool IsSynced { get; set; }
    }
}
