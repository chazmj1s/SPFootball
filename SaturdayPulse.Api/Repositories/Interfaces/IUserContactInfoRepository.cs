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
    }
}
