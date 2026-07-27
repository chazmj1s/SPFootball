using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
{
    public interface IUserContactInfoRepository
    {
        Task<UserContactInfo?> GetByUserIdAsync(string userId, CancellationToken token = default);
        Task<bool> IsEmailAvailableAsync(string email, string? excludingUserId = null, CancellationToken token = default);
        Task CreateAsync(UserContactInfo contactInfo, CancellationToken token = default);
        Task UpdateEmailAsync(string userId, string newEmail, CancellationToken token = default);
        Task UpdatePhoneAsync(string userId, string? newPhoneNumber, CancellationToken token = default);
        Task UpdateSmsConsentAsync(string userId, bool consent, string? source, CancellationToken token = default);

        /// <summary>
        /// No source parameter, unlike UpdateSmsConsentAsync - UserContactInfo
        /// only has MarketingEmailConsent/MarketingEmailConsentAt, no
        /// MarketingEmailConsentSource field (that's SMS-only, presumably for
        /// TCPA reasons that don't apply to email).
        /// </summary>
        Task UpdateEmailConsentAsync(string userId, bool consent, CancellationToken token = default);

        /// <summary>Part of Delete Account - removes this user's row entirely.</summary>
        Task DeleteAsync(string userId, CancellationToken token = default);
    }
}
