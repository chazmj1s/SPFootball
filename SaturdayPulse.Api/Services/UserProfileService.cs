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

        // Beta grants all expire on the same fixed date, per the 2026-07-25
        // session decision (see project handoff notes). Not user/grant-specific.
        public static readonly DateTime BetaExpiryDate = new(2027, 7, 31);

        /// <summary>
        /// Returns the current profile for this identity, or null if none
        /// exists. NEVER creates anything — this backs the Login action's
        /// lookup. A null here means "no account for this identity," which
        /// the caller (UserController.GetMe -> 404) surfaces to the person
        /// rather than silently provisioning one. Safe to call from app
        /// startup / any polling path with no risk of side effects.
        /// </summary>
        public async Task<UserProfileResponse?> GetProfileAsync(string userId, CancellationToken token = default)
        {
            var profile = await _uow.UserProfiles.GetByUserIdAsync(userId, token);
            if (profile == null) return null;

            var contact = await _uow.UserContactInfo.GetByUserIdAsync(userId, token);
            var activeEntitlement = await _uow.Entitlements.GetActiveCfbSeasonPassAsync(userId, token);
            var allEntitlements = await _uow.Entitlements.GetByUserIdAsync(userId, token);

            return ToResponse(profile, contact!, activeEntitlement, allEntitlements);
        }

        /// <summary>
        /// Creates a NEW profile for this identity. THE ONLY method that
        /// creates a UserProfile — call this only immediately after Auth0
        /// signup completes (the explicit Create Account action), never
        /// after a plain login and never from a passive fetch. Throws
        /// InvalidOperationException (-> 409 at the controller) if a profile
        /// already exists for this identity, or if the given email is
        /// already in use by a different account — same conflict shape as
        /// UpdateEmailAsync/UpdateHandleAsync below, not a new error type.
        ///
        /// Previously this behavior lived inside a "get or create" method
        /// called from every GetMe request, which meant any unrecognized
        /// identity — including a stale/mismatched dev fallback GUID —
        /// silently spawned a new blank profile with no user awareness.
        /// See session-handoff-2026-07-22.
        /// </summary>
        public async Task<UserProfileResponse> CreateProfileAsync(
            string userId, string? email, CancellationToken token = default)
        {
            var existing = await _uow.UserProfiles.GetByUserIdAsync(userId, token);
            if (existing != null)
                throw new InvalidOperationException("An account already exists for this login.");

            if (email != null && !await _uow.UserContactInfo.IsEmailAvailableAsync(email, excludingUserId: null, token))
                throw new InvalidOperationException("That email is already in use.");

            var profile = new UserProfile
            {
                UserId = userId,
                Handle = $"user_{userId[..Math.Min(8, userId.Length)]}"
            };
            await _uow.UserProfiles.CreateAsync(profile, token);

            var contact = new UserContactInfo
            {
                UserId = userId,
                Email = email ?? $"{userId}@unset.local"
            };
            await _uow.UserContactInfo.CreateAsync(contact, token);

            await _uow.AccountAuditLogs.AddAsync(new AccountAuditLog
            {
                UserId = userId,
                EventType = "AccountCreated",
                EventAt = DateTime.UtcNow
            }, token);

            await _uow.SaveChangesAsync(token);
            _logger.LogInformation("Created new UserProfile for {UserId}", userId);

            // Brand new — never has an entitlement yet, no lookup needed.
            return ToResponse(profile, contact, activeEntitlement: null, allEntitlements: new List<UserEntitlement>());
        }
        /// <summary>
        /// Admin-only dev toggle for the CFB Season Pass entitlement — lets an
        /// admin flip their own entitlement on/off without a real purchase, to
        /// verify both experiences without jumping through Stripe. Throws
        /// UnauthorizedAccessException for any non-admin caller; UserController
        /// maps that to a 403. Reuses the existing Entitlements repository
        /// methods (GetActiveCfbSeasonPassAsync / AddAsync) rather than adding
        /// new ones — toggling off expires the existing row (sets ExpiryDate to
        /// now) instead of deleting it, so there's a history of grants/revokes.
        /// </summary>
        public async Task<UserProfileResponse> SetDevEntitlementAsync(
            string userId, bool enabled, CancellationToken token = default)
        {
            var profile = await _uow.UserProfiles.GetByUserIdAsync(userId, token);
            if (profile == null || !profile.IsAdmin)
                throw new UnauthorizedAccessException("Only admins can use the dev entitlement toggle.");

            var active = await _uow.Entitlements.GetActiveCfbSeasonPassAsync(userId, token);

            if (enabled)
            {
                if (active != null)
                {
                    active.ExpiryDate = new DateTime(2999, 12, 31);
                }
                else
                {
                    await _uow.Entitlements.AddAsync(new UserEntitlement
                    {
                        UserId = userId,
                        ProductKey = "cfb-season-pass",
                        ExpiryDate = new DateTime(2999, 12, 31),
                        Source = "manual-grant"
                    }, token);
                }
            }
            else if (active != null)
            {
                active.ExpiryDate = DateTime.UtcNow;
            }

            await _uow.SaveChangesAsync(token);

            var contact = await _uow.UserContactInfo.GetByUserIdAsync(userId, token);
            var refreshedActive = await _uow.Entitlements.GetActiveCfbSeasonPassAsync(userId, token);
            var allEntitlements = await _uow.Entitlements.GetByUserIdAsync(userId, token);

            return ToResponse(profile, contact!, refreshedActive, allEntitlements);
        }

        // ── Admin: Users page (list + grant/revoke beta access) ──────────────
        // No Angular equivalent - new admin console capability. Lives here
        // rather than in a separate service since it's the same UserProfile/
        // Entitlements data this class already owns.

        /// <summary>
        /// Every user profile plus every entitlement they hold (any product,
        /// any status) for the admin console's Users page. N+1 queries against
        /// UserContactInfo/Entitlements per user - fine at current scale
        /// (single admin, pre-beta); revisit with a batched query if the user
        /// count grows large enough for it to matter.
        /// </summary>
        public async Task<List<AdminUserSummaryResponse>> GetAllUsersWithEntitlementsAsync(CancellationToken token = default)
        {
            var profiles = await _uow.UserProfiles.GetAllAsync(token);
            var result = new List<AdminUserSummaryResponse>(profiles.Count);

            foreach (var profile in profiles)
            {
                var contact = await _uow.UserContactInfo.GetByUserIdAsync(profile.UserId, token);
                var entitlements = await _uow.Entitlements.GetByUserIdAsync(profile.UserId, token);

                result.Add(new AdminUserSummaryResponse
                {
                    UserId = profile.UserId,
                    Handle = profile.Handle,
                    Email = contact?.Email,
                    IsAdmin = profile.IsAdmin,
                    Entitlements = entitlements.Select(e => new AdminEntitlementResponse
                    {
                        ProductKey = e.ProductKey,
                        Source = e.Source,
                        ExpiryDate = e.ExpiryDate,
                        PassYear = e.PassYear,
                        IsActive = e.ExpiryDate.HasValue && e.ExpiryDate.Value > DateTime.UtcNow
                    }).ToList()
                });
            }

            return result;
        }

        /// <summary>
        /// Grants (or extends) beta access to the given product for a user.
        /// Not hardcoded to CFB - takes productKey as a parameter, per
        /// UserEntitlement's own class summary ("ProductKey stays on the schema
        /// so a future league doesn't require a migration"). If the user
        /// already has an active grant for this product, extends it to
        /// BetaExpiryDate rather than stacking a second row.
        /// </summary>
        /// <summary>
        /// Builds the season-specific ProductKey stored on real grants (e.g.
        /// "cfb-season-pass-2026"), matching Apple/Google IAP's per-season SKU
        /// convention. The admin dev-toggle sentinel deliberately does NOT use
        /// this - it's not tied to a real season, so it keeps the bare key.
        /// Queries that need "does this user have ANY active pass for this
        /// product, any season" match on the base key as a prefix instead of
        /// an exact match - see UserEntitlementRepository.GetActiveCfbSeasonPassAsync
        /// and RevokeAccessAsync below.
        /// </summary>
        private static string SeasonedProductKey(string baseProductKey, int season) => $"{baseProductKey}-{season}";

        public async Task GrantBetaAccessAsync(string userId, string productKey, CancellationToken token = default)
        {
            var profile = await _uow.UserProfiles.GetByUserIdAsync(userId, token);
            if (profile == null)
                throw new InvalidOperationException("No such user.");

            // Beta covers the upcoming season implied by BetaExpiryDate (7/31/2027 -> season 2026).
            var betaSeason = BetaExpiryDate.Year - 1;
            var seasonedKey = SeasonedProductKey(productKey, betaSeason);

            var existing = await _uow.Entitlements.GetByUserIdAsync(userId, token);
            var active = existing.FirstOrDefault(e =>
                e.ProductKey == seasonedKey && e.ExpiryDate.HasValue && e.ExpiryDate.Value > DateTime.UtcNow);

            if (active != null)
            {
                active.ExpiryDate = BetaExpiryDate;
            }
            else
            {
                await _uow.Entitlements.AddAsync(new UserEntitlement
                {
                    UserId = userId,
                    ProductKey = seasonedKey,
                    ExpiryDate = BetaExpiryDate,
                    PassYear = betaSeason,
                    Source = "beta"
                }, token);
            }

            await _uow.AccountAuditLogs.AddAsync(new AccountAuditLog
            {
                UserId = userId,
                EventType = "SeasonPassGranted",
                EventAt = DateTime.UtcNow,
                ProductKey = seasonedKey,
                PassYear = betaSeason,
                Source = "beta"
            }, token);

            await _uow.SaveChangesAsync(token);
        }

        /// <summary>
        /// Grants a season pass for a specific season year - an ad hoc admin
        /// tool for special cases (comps, press access, etc.), distinct from
        /// GrantBetaAccessAsync. Unlike that method, this always inserts a new
        /// row rather than extending an existing active one - the whole point
        /// is to let a user accumulate distinct, dated season grants that show
        /// up as separate history entries, not a single rolling expiry.
        /// ExpiryDate = July 31 of the year after the given season, matching
        /// the same convention used for beta grants (a season pass covers the
        /// season plus a buffer into the following offseason).
        /// </summary>
        public async Task GrantSeasonPassAsync(string userId, string productKey, int season, CancellationToken token = default)
        {
            var profile = await _uow.UserProfiles.GetByUserIdAsync(userId, token);
            if (profile == null)
                throw new InvalidOperationException("No such user.");

            await _uow.Entitlements.AddAsync(new UserEntitlement
            {
                UserId = userId,
                ProductKey = SeasonedProductKey(productKey, season),
                ExpiryDate = new DateTime(season + 1, 7, 31),
                PassYear = season,
                Source = "manual-grant"
            }, token);

            await _uow.AccountAuditLogs.AddAsync(new AccountAuditLog
            {
                UserId = userId,
                EventType = "SeasonPassGranted",
                EventAt = DateTime.UtcNow,
                ProductKey = SeasonedProductKey(productKey, season),
                PassYear = season,
                Source = "manual-grant"
            }, token);

            await _uow.SaveChangesAsync(token);
        }

        /// <summary>
        /// Revokes (expires) every currently active entitlement matching this
        /// product, by prefix - not just one. Sets ExpiryDate to now rather
        /// than deleting rows - same "keep grant/revoke history" convention as
        /// SetDevEntitlementAsync's toggle-off path. Prefix match (not exact)
        /// because a user can now have multiple active seasoned grants at once
        /// (e.g. both a 2025 and 2026 pass overlapping); "revoke" means "this
        /// user should no longer have access," which should clear all of them,
        /// not silently leave a second one active.
        /// </summary>
        public async Task RevokeAccessAsync(string userId, string productKey, CancellationToken token = default)
        {
            var existing = await _uow.Entitlements.GetByUserIdAsync(userId, token);
            var activeMatches = existing.Where(e =>
                e.ProductKey.StartsWith(productKey) && e.ExpiryDate.HasValue && e.ExpiryDate.Value > DateTime.UtcNow).ToList();

            if (activeMatches.Count == 0) return;

            var now = DateTime.UtcNow;
            foreach (var entitlement in activeMatches)
                entitlement.ExpiryDate = now;

            await _uow.SaveChangesAsync(token);
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

        /// <summary>
        /// Standalone consent toggle - unlike phone/SMS consent (bundled into
        /// UpdatePhoneAsync since there's no separate endpoint for it), email
        /// consent gets its own endpoint because the new inline Settings
        /// checkbox saves immediately on tap, with no accompanying "change
        /// your email" action to piggyback on.
        /// </summary>
        public async Task UpdateEmailConsentAsync(string userId, bool consent, CancellationToken token = default)
        {
            await _uow.UserContactInfo.UpdateEmailConsentAsync(userId, consent, token);
            await _uow.SaveChangesAsync(token);
        }

        /// <summary>
        /// Permanently deletes the account and every related row - UserProfile,
        /// UserContactInfo, FollowedTeams, FollowedGames, UserEntitlements.
        /// Writes an AccountDeleted row to AccountAuditLogs first, in the same
        /// transaction, so the deletion and its audit record either both
        /// happen or neither does. AccountAuditLogs itself is deliberately
        /// untouched - no FK to UserProfile, so this row (and any prior
        /// AccountCreated/SeasonPassGranted rows for this UserId) survive.
        /// </summary>
        public async Task DeleteAccountAsync(string userId, CancellationToken token = default)
        {
            var profile = await _uow.UserProfiles.GetByUserIdAsync(userId, token);
            if (profile == null)
                throw new InvalidOperationException("No such user.");

            await _uow.AccountAuditLogs.AddAsync(new AccountAuditLog
            {
                UserId = userId,
                EventType = "AccountDeleted",
                EventAt = DateTime.UtcNow
            }, token);

            await _uow.FollowedTeams.DeleteAllForUserAsync(userId, token);
            await _uow.FollowedGames.DeleteAllForUserAsync(userId, token);
            await _uow.Entitlements.DeleteAllForUserAsync(userId, token);
            await _uow.UserContactInfo.DeleteAsync(userId, token);
            await _uow.UserProfiles.DeleteAsync(userId, token);

            await _uow.SaveChangesAsync(token);
            _logger.LogInformation("Deleted account and all associated data for {UserId}", userId);
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

        private static UserProfileResponse ToResponse(
            UserProfile profile, UserContactInfo contact, UserEntitlement? activeEntitlement,
            List<UserEntitlement> allEntitlements) => new()
        {
            UserId = profile.UserId,
            Handle = profile.Handle,
            PrimaryTeamId = profile.PrimaryTeamId,
            ExpiryDate = activeEntitlement?.ExpiryDate,
            IsEntitled = activeEntitlement != null,
            IsAdmin = profile.IsAdmin,
            Email = contact.Email,
            EmailVerified = contact.EmailVerifiedAt.HasValue,
            PhoneNumber = contact.PhoneNumber,
            PhoneVerified = contact.PhoneVerifiedAt.HasValue,
            MarketingSmsConsent = contact.MarketingSmsConsent,
            MarketingEmailConsent = contact.MarketingEmailConsent,
            Entitlements = allEntitlements.Select(e => new EntitlementSummary
            {
                ProductKey = e.ProductKey,
                Source = e.Source,
                ExpiryDate = e.ExpiryDate,
                PassYear = e.PassYear,
                IsActive = e.ExpiryDate.HasValue && e.ExpiryDate.Value > DateTime.UtcNow
            }).ToList()
        };
    }
}
