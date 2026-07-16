using System.Text.RegularExpressions;
using SaturdayPulse.Contracts;
using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Business logic for profile, contact info, and follows. Controller stays
    /// a thin HTTP wrapper — validation, uniqueness checks, and provisioning
    /// all live here, same split as ProductionGameDataService.
    ///
    /// Error signaling follows ProductionGameDataService's convention: throw,
    /// don't return a result tuple. ArgumentException -> bad input,
    /// InvalidOperationException -> conflict (handle/email already taken).
    /// </summary>
    public class UserProfileService(
        IUnitOfWork uow,
        ILogger<UserProfileService> logger)
    {
        private readonly IUnitOfWork _uow = uow;
        private readonly ILogger<UserProfileService> _logger = logger;

        // 3–20 chars, alphanumeric + underscore.
        private static readonly Regex HandlePattern = new(@"^[A-Za-z0-9_]{3,20}$", RegexOptions.Compiled);
        private static readonly Regex E164Pattern = new(@"^\+[1-9]\d{6,14}$", RegexOptions.Compiled);

        /// <summary>
        /// Returns the current profile, provisioning a new one on first-ever
        /// contact from this UserId.
        /// </summary>
        public async Task<UserProfileResponse> GetOrCreateProfileAsync(
            string userId, string? defaultEmail, CancellationToken token = default)
        {
            var profile = await _uow.UserProfiles.GetByUserIdAsync(userId, token);
            var contact = await _uow.UserContactInfo.GetByUserIdAsync(userId, token);

            if (profile == null)
            {
                profile = new UserProfile
                {
                    UserId = userId,
                    Handle = $"user_{userId[..Math.Min(8, userId.Length)]}"
                };
                await _uow.UserProfiles.CreateAsync(profile, token);

                contact = new UserContactInfo
                {
                    UserId = userId,
                    Email = defaultEmail ?? $"{userId}@unset.local"
                };
                await _uow.UserContactInfo.CreateAsync(contact, token);

                await _uow.SaveChangesAsync(token);
                _logger.LogInformation("Provisioned new UserProfile for {UserId}", userId);
            }

            return ToResponse(profile, contact!);
        }

        public async Task UpdateHandleAsync(string userId, string newHandle, CancellationToken token = default)
        {
            if (!HandlePattern.IsMatch(newHandle))
                throw new ArgumentException("Handle must be 3-20 characters, letters/numbers/underscore only.");

            if (!await _uow.UserProfiles.IsHandleAvailableAsync(newHandle, userId, token))
                throw new InvalidOperationException("That handle is already taken.");

            await _uow.UserProfiles.UpdateHandleAsync(userId, newHandle, token);
            await _uow.SaveChangesAsync(token);
        }

        public async Task UpdatePrimaryTeamAsync(string userId, int? teamId, CancellationToken token = default)
        {
            await _uow.UserProfiles.UpdatePrimaryTeamAsync(userId, teamId, token);
            await _uow.SaveChangesAsync(token);
        }

        public async Task UpdateEmailAsync(string userId, string newEmail, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(newEmail) || !newEmail.Contains('@'))
                throw new ArgumentException("A valid email address is required.");

            if (!await _uow.UserContactInfo.IsEmailAvailableAsync(newEmail, userId, token))
                throw new InvalidOperationException("That email is already in use.");

            await _uow.UserContactInfo.UpdateEmailAsync(userId, newEmail, token);
            await _uow.SaveChangesAsync(token);
        }

        public async Task UpdatePhoneAsync(
            string userId, string? phoneNumber, bool? smsConsent, CancellationToken token = default)
        {
            var normalized = NormalizeToE164(phoneNumber);

            if (normalized != null && !E164Pattern.IsMatch(normalized))
                throw new ArgumentException(
                    "Couldn't recognize that as a phone number. Try including the area code, e.g. (512) 555-1234.");

            await _uow.UserContactInfo.UpdatePhoneAsync(userId, normalized, token);

            if (smsConsent.HasValue)
                await _uow.UserContactInfo.UpdateSmsConsentAsync(
                    userId, smsConsent.Value, smsConsent.Value ? "settings_phone_update" : null, token);

            await _uow.SaveChangesAsync(token);
        }

        /// <summary>
        /// Accepts whatever a person naturally types — "(512) 555-1234",
        /// "512-555-1234", "5125551234", already-E.164 "+15125551234" — and
        /// normalizes to E.164 for storage/Twilio compatibility. Defaults to
        /// US country code (+1) when none is given, since that covers the
        /// overwhelming majority of this app's users. Returns null for null/
        /// empty input (clearing the phone number), and returns the input
        /// unchanged if it already starts with '+' — assume the person who
        /// typed a country code knows what they're doing, don't second-guess it.
        /// </summary>
        private static string? NormalizeToE164(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            var trimmed = input.Trim();
            if (trimmed.StartsWith('+')) return trimmed;

            var digitsOnly = new string(trimmed.Where(char.IsDigit).ToArray());

            return digitsOnly.Length switch
            {
                10 => $"+1{digitsOnly}",                 // 5125551234 -> +15125551234
                11 when digitsOnly[0] == '1' => $"+{digitsOnly}", // 15125551234 -> +15125551234
                _ => trimmed // couldn't confidently normalize — pass through, let E164Pattern reject it with a clear error
            };
        }

        // ── Follows ───────────────────────────────────────────────────────

        public async Task FollowTeamAsync(string userId, int teamId, CancellationToken token = default)
        {
            await _uow.FollowedTeams.FollowAsync(userId, teamId, token);
            await _uow.SaveChangesAsync(token);
        }

        public async Task UnfollowTeamAsync(string userId, int teamId, CancellationToken token = default)
        {
            await _uow.FollowedTeams.UnfollowAsync(userId, teamId, token);
            await _uow.SaveChangesAsync(token);
        }

        public Task<List<FollowedTeam>> GetFollowedTeamsAsync(string userId, CancellationToken token = default)
            => _uow.FollowedTeams.GetByUserIdAsync(userId, token);

        public async Task FollowGameAsync(string userId, int team1Id, int team2Id, CancellationToken token = default)
        {
            await _uow.FollowedGames.FollowAsync(userId, team1Id, team2Id, token);
            await _uow.SaveChangesAsync(token);
        }

        public async Task UnfollowGameAsync(string userId, int team1Id, int team2Id, CancellationToken token = default)
        {
            await _uow.FollowedGames.UnfollowAsync(userId, team1Id, team2Id, token);
            await _uow.SaveChangesAsync(token);
        }

        public Task<List<FollowedGame>> GetFollowedGamesAsync(string userId, CancellationToken token = default)
            => _uow.FollowedGames.GetByUserIdAsync(userId, token);

        private static UserProfileResponse ToResponse(UserProfile profile, UserContactInfo contact) => new()
        {
            UserId = profile.UserId,
            Handle = profile.Handle,
            PrimaryTeamId = profile.PrimaryTeamId,
            ExpiryDate = profile.ExpiryDate,
            Email = contact.Email,
            EmailVerified = contact.EmailVerifiedAt.HasValue,
            PhoneNumber = contact.PhoneNumber,
            PhoneVerified = contact.PhoneVerifiedAt.HasValue,
            MarketingSmsConsent = contact.MarketingSmsConsent
        };
    }
}
