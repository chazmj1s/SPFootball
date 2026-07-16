using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
{
    public class UserContactInfoRepository : IUserContactInfoRepository
    {
        private readonly NCAAContext _context;
        public UserContactInfoRepository(NCAAContext context) => _context = context;

        public Task<UserContactInfo?> GetByUserIdAsync(string userId, CancellationToken token = default)
            => _context.UserContactInfos.FirstOrDefaultAsync(c => c.UserId == userId, token);

        public async Task<bool> IsEmailAvailableAsync(string email, string? excludingUserId = null, CancellationToken token = default)
        {
            // Email column is NOCASE-collated, so this comparison is
            // case-insensitive at the DB level already — no ToLower() needed,
            // same pattern as Handle.
            var query = _context.UserContactInfos.Where(c => c.Email == email);

            if (excludingUserId != null)
                query = query.Where(c => c.UserId != excludingUserId);

            return !await query.AnyAsync(token);
        }

        public async Task CreateAsync(UserContactInfo contactInfo, CancellationToken token = default)
        {
            contactInfo.UpdatedAt = DateTime.UtcNow;
            await _context.UserContactInfos.AddAsync(contactInfo, token);
        }

        public async Task UpdateEmailAsync(string userId, string newEmail, CancellationToken token = default)
        {
            var contact = await _context.UserContactInfos
                .FirstOrDefaultAsync(c => c.UserId == userId, token);

            if (contact == null) return;

            contact.Email = newEmail;
            contact.EmailVerifiedAt = null; // clear on edit — never let a stale verification survive a change
            contact.UpdatedAt = DateTime.UtcNow;
            contact.IsSynced = false;
        }

        public async Task UpdatePhoneAsync(string userId, string? newPhoneNumber, CancellationToken token = default)
        {
            var contact = await _context.UserContactInfos
                .FirstOrDefaultAsync(c => c.UserId == userId, token);

            if (contact == null) return;

            contact.PhoneNumber = newPhoneNumber;
            contact.PhoneVerifiedAt = null; // clear on edit, same reasoning as Email above
            contact.UpdatedAt = DateTime.UtcNow;
            contact.IsSynced = false;
            // MarketingSmsConsent is intentionally left untouched here — consent
            // is to be texted, not tied to a specific number.
        }

        public async Task UpdateSmsConsentAsync(string userId, bool consent, string? source, CancellationToken token = default)
        {
            var contact = await _context.UserContactInfos
                .FirstOrDefaultAsync(c => c.UserId == userId, token);

            if (contact == null) return;

            contact.MarketingSmsConsent = consent;
            contact.MarketingSmsConsentAt = consent ? DateTime.UtcNow : contact.MarketingSmsConsentAt;
            contact.MarketingSmsConsentSource = consent ? source : contact.MarketingSmsConsentSource;
            contact.UpdatedAt = DateTime.UtcNow;
            contact.IsSynced = false;
        }
    }
}
