using SaturdayPulse.Contracts;
using SaturdayPulse.Contracts.Requests;
using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Turns RevenueCat webhook events into UserEntitlement rows.
    ///
    /// Grants are computed deterministically from the event's purchase date
    /// (SeasonPassExpiryCalculator.GetNextExpiry with no current expiry), NOT
    /// from the user's current entitlement, so a redelivered event is a no-op
    /// instead of extending the pass a second time. Row identity is
    /// (UserId, seasoned ProductKey), e.g. "cfb-season-pass-2026".
    ///
    /// Handled: INITIAL_PURCHASE and RENEWAL (grant), CANCELLATION with reason
    /// CUSTOMER_SUPPORT (refund - revoke the purchase-backed row). Everything
    /// else is acknowledged and logged only: an ordinary cancel/expiry needs no
    /// change because the season-aligned ExpiryDate already bounds access.
    /// </summary>
    public class RevenueCatWebhookService(
        IUnitOfWork uow,
        ILogger<RevenueCatWebhookService> logger)
    {
        private const string BaseProductKey = "cfb-season-pass";
        private const string StoreProductPrefix = "cfb_season_pass";
        private const string AnonymousIdPrefix = "$RCAnonymousID:";
        private const string RefundCancelReason = "CUSTOMER_SUPPORT";

        private readonly IUnitOfWork _uow = uow;
        private readonly ILogger<RevenueCatWebhookService> _logger = logger;

        public async Task HandleAsync(RevenueCatWebhookEvent evt, CancellationToken token = default)
        {
            switch (evt.Type)
            {
                case "INITIAL_PURCHASE":
                case "RENEWAL":
                    await GrantAsync(evt, token);
                    break;

                case "CANCELLATION" when evt.CancelReason == RefundCancelReason:
                    await RevokeAsync(evt, token);
                    break;

                default:
                    _logger.LogInformation(
                        "RevenueCat event {Type} ({EventId}, {Environment}) acknowledged; no action.",
                        evt.Type, evt.Id, evt.Environment);
                    break;
            }
        }

        private async Task GrantAsync(RevenueCatWebhookEvent evt, CancellationToken token)
        {
            var target = await ResolveTargetAsync(evt, token);
            if (target == null) return;

            var (userId, seasonedKey, season, expiry, source) = target.Value;

            var existing = await _uow.Entitlements.GetByUserIdAsync(userId, token);
            var row = existing.FirstOrDefault(e => e.ProductKey == seasonedKey);

            if (row == null)
            {
                await _uow.Entitlements.AddAsync(new UserEntitlement
                {
                    UserId = userId,
                    ProductKey = seasonedKey,
                    ExpiryDate = expiry,
                    PassYear = season,
                    Source = source
                }, token);
            }
            else if (row.Source == source && row.ExpiryDate.HasValue && row.ExpiryDate.Value >= expiry)
            {
                _logger.LogInformation(
                    "RevenueCat {Type} ({EventId}) already applied for {UserId} {ProductKey}; no change.",
                    evt.Type, evt.Id, userId, seasonedKey);
                return;
            }
            else
            {
                // Existing row for the same season (e.g. a beta or manual grant,
                // or a previously refunded purchase): keep the later expiry and
                // record that it is now purchase-backed.
                row.Source = source;
                row.PassYear = season;
                if (!row.ExpiryDate.HasValue || row.ExpiryDate.Value < expiry)
                    row.ExpiryDate = expiry;
            }

            await _uow.AccountAuditLogs.AddAsync(new AccountAuditLog
            {
                UserId = userId,
                EventType = "SeasonPassGranted",
                EventAt = DateTime.UtcNow,
                ProductKey = seasonedKey,
                PassYear = season,
                Source = source
            }, token);

            await _uow.SaveChangesAsync(token);

            _logger.LogInformation(
                "RevenueCat {Type} ({EventId}, {Environment}) granted {ProductKey} to {UserId}, expires {Expiry:yyyy-MM-dd}.",
                evt.Type, evt.Id, evt.Environment, seasonedKey, userId, expiry);
        }

        private async Task RevokeAsync(RevenueCatWebhookEvent evt, CancellationToken token)
        {
            var target = await ResolveTargetAsync(evt, token);
            if (target == null) return;

            var (userId, seasonedKey, season, _, source) = target.Value;

            var now = DateTime.UtcNow;
            var existing = await _uow.Entitlements.GetByUserIdAsync(userId, token);

            // Only purchase-backed rows from this store are revoked - never a
            // beta or manual-grant row that happens to share the season.
            var row = existing.FirstOrDefault(e =>
                e.ProductKey == seasonedKey &&
                e.Source == source &&
                e.ExpiryDate.HasValue && e.ExpiryDate.Value > now);

            if (row == null)
            {
                _logger.LogInformation(
                    "RevenueCat refund ({EventId}) for {UserId} {ProductKey}: no active purchase-backed row; no change.",
                    evt.Id, userId, seasonedKey);
                return;
            }

            row.ExpiryDate = now;

            await _uow.AccountAuditLogs.AddAsync(new AccountAuditLog
            {
                UserId = userId,
                EventType = "SeasonPassRevoked",
                EventAt = now,
                ProductKey = seasonedKey,
                PassYear = season,
                Source = source
            }, token);

            await _uow.SaveChangesAsync(token);

            _logger.LogInformation(
                "RevenueCat refund ({EventId}, {Environment}) revoked {ProductKey} for {UserId}.",
                evt.Id, evt.Environment, seasonedKey, userId);
        }

        /// <summary>
        /// Validates the event and derives the entitlement identity from it.
        /// Returns null (after logging) for events that can't or shouldn't be
        /// applied: wrong product, anonymous/unknown user, missing purchase date.
        /// These are acknowledged, not retried - retrying can't fix them.
        /// </summary>
        private async Task<(string UserId, string SeasonedKey, int Season, DateTime Expiry, string Source)?> ResolveTargetAsync(
            RevenueCatWebhookEvent evt, CancellationToken token)
        {
            if (string.IsNullOrEmpty(evt.ProductId) ||
                !evt.ProductId.StartsWith(StoreProductPrefix, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "RevenueCat {Type} ({EventId}): unrecognized product '{ProductId}'; ignored.",
                    evt.Type, evt.Id, evt.ProductId);
                return null;
            }

            var userId = evt.AppUserId;
            if (string.IsNullOrWhiteSpace(userId) ||
                userId.StartsWith(AnonymousIdPrefix, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "RevenueCat {Type} ({EventId}): anonymous or missing app_user_id; ignored.",
                    evt.Type, evt.Id);
                return null;
            }

            var profile = await _uow.UserProfiles.GetByUserIdAsync(userId, token);
            if (profile == null)
            {
                _logger.LogWarning(
                    "RevenueCat {Type} ({EventId}): no UserProfile for {UserId}; ignored.",
                    evt.Type, evt.Id, userId);
                return null;
            }

            if (evt.PurchasedAtMs is null)
            {
                _logger.LogWarning(
                    "RevenueCat {Type} ({EventId}): missing purchased_at_ms; ignored.",
                    evt.Type, evt.Id);
                return null;
            }

            var purchasedAt = DateTimeOffset.FromUnixTimeMilliseconds(evt.PurchasedAtMs.Value).UtcDateTime;
            var expiry = SeasonPassExpiryCalculator.GetNextExpiry(BaseProductKey, null, purchasedAt);
            var season = expiry.Year - 1;

            return (userId, $"{BaseProductKey}-{season}", season, expiry, MapSource(evt.Store));
        }

        private static string MapSource(string? store) => store switch
        {
            "APP_STORE" or "MAC_APP_STORE" => "apple",
            "PLAY_STORE" => "google",
            "TEST_STORE" => "revenuecat-test",
            _ => "iap"
        };
    }
}
