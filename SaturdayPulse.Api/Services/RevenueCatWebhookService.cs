using SaturdayPulse.Contracts;
using SaturdayPulse.Contracts.Requests;
using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Turns RevenueCat webhook events into UserEntitlement rows.
    ///
    /// The pass is an annual subscription: access lasts until the end of the
    /// period the store says was paid for (the event's expiration_at_ms).
    /// Grants are computed deterministically from the event itself (its
    /// purchase and expiration dates), NOT from the user's current entitlement,
    /// so a redelivered event is a no-op instead of extending the pass a second
    /// time. The season-aligned date (7/31 after the season the purchase falls
    /// in, via SeasonPassExpiryCalculator) is kept only as a FLOOR: the later of
    /// the two wins. In production the store period always ends later, so the
    /// floor matters only for sandbox/test renewals that run on an accelerated
    /// clock. Row identity is (UserId, seasoned ProductKey), e.g.
    /// "cfb-season-pass-2026"; the season in the key is a label taken from the
    /// purchase date, not an access boundary.
    ///
    /// Handled: INITIAL_PURCHASE and RENEWAL (grant), CANCELLATION with reason
    /// CUSTOMER_SUPPORT (refund - revoke the purchase-backed row), and TRANSFER
    /// (the same store account bought for a different App User ID, so
    /// RevenueCat moved the subscription: move the purchase-backed rows to the
    /// new user). Everything else is acknowledged and logged only: an ordinary
    /// cancel/expiry needs no change because ExpiryDate already ends at the
    /// paid period's end.
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

                case "TRANSFER":
                    await TransferAsync(evt, token);
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
        /// TRANSFER: RevenueCat moved a store subscription from one App User ID to
        /// another (for example a sandbox Apple ID that already bought the pass
        /// for one login is now used with a different login). The event carries no
        /// product or dates, but the purchase-backed rows are already in our own
        /// table, so they are moved from the old user(s) to the new one, keeping
        /// their expiry. Only rows from the event's store that are still active are
        /// moved; beta and manual-grant rows are never touched. If the destination
        /// already has a row for the same product, the later expiry wins, the row
        /// is marked purchase-backed, and the source row is ended. A redelivered
        /// event finds nothing left to move and changes nothing.
        /// </summary>
        private async Task TransferAsync(RevenueCatWebhookEvent evt, CancellationToken token)
        {
            var fromIds = CleanUserIds(evt.TransferredFrom);
            var toIds = CleanUserIds(evt.TransferredTo);

            if (fromIds.Count == 0 || toIds.Count == 0)
            {
                _logger.LogInformation(
                    "RevenueCat TRANSFER ({EventId}, {Environment}): no real source or destination user (from {FromCount}, to {ToCount}); no action.",
                    evt.Id, evt.Environment, fromIds.Count, toIds.Count);
                return;
            }

            string? destinationId = null;
            foreach (var id in toIds)
            {
                if (await _uow.UserProfiles.GetByUserIdAsync(id, token) != null)
                {
                    destinationId = id;
                    break;
                }
            }

            if (destinationId == null)
            {
                _logger.LogWarning(
                    "RevenueCat TRANSFER ({EventId}): no UserProfile for any destination user; ignored.",
                    evt.Id);
                return;
            }

            var source = MapSource(evt.Store);
            var now = DateTime.UtcNow;
            var destinationRows = await _uow.Entitlements.GetByUserIdAsync(destinationId, token);
            var moved = 0;

            foreach (var fromId in fromIds.Where(f => f != destinationId))
            {
                var sourceRows = await _uow.Entitlements.GetByUserIdAsync(fromId, token);
                var movable = sourceRows
                    .Where(e => e.Source == source &&
                                e.ExpiryDate.HasValue && e.ExpiryDate.Value > now)
                    .ToList();

                foreach (var row in movable)
                {
                    var existing = destinationRows.FirstOrDefault(e => e.ProductKey == row.ProductKey);

                    if (existing == null)
                    {
                        row.UserId = destinationId;
                        destinationRows.Add(row);
                    }
                    else
                    {
                        existing.Source = row.Source;
                        existing.PassYear = row.PassYear;
                        if (!existing.ExpiryDate.HasValue || existing.ExpiryDate.Value < row.ExpiryDate!.Value)
                            existing.ExpiryDate = row.ExpiryDate;

                        row.ExpiryDate = now;
                    }

                    await _uow.AccountAuditLogs.AddAsync(new AccountAuditLog
                    {
                        UserId = fromId,
                        EventType = "SeasonPassTransferOut",
                        EventAt = now,
                        ProductKey = row.ProductKey,
                        PassYear = row.PassYear,
                        Source = source
                    }, token);

                    await _uow.AccountAuditLogs.AddAsync(new AccountAuditLog
                    {
                        UserId = destinationId,
                        EventType = "SeasonPassTransferIn",
                        EventAt = now,
                        ProductKey = row.ProductKey,
                        PassYear = row.PassYear,
                        Source = source
                    }, token);

                    moved++;
                }
            }

            if (moved == 0)
            {
                _logger.LogInformation(
                    "RevenueCat TRANSFER ({EventId}, {Environment}) to {UserId}: no active {Source} rows to move; no change.",
                    evt.Id, evt.Environment, destinationId, source);
                return;
            }

            await _uow.SaveChangesAsync(token);

            _logger.LogInformation(
                "RevenueCat TRANSFER ({EventId}, {Environment}) moved {Count} {Source} entitlement row(s) to {UserId}.",
                evt.Id, evt.Environment, moved, source, destinationId);
        }

        /// <summary>Real App User IDs only: drops blanks and RevenueCat anonymous ids.</summary>
        private static List<string> CleanUserIds(List<string>? ids) =>
            (ids ?? [])
                .Where(i => !string.IsNullOrWhiteSpace(i) &&
                            !i.StartsWith(AnonymousIdPrefix, StringComparison.Ordinal))
                .Distinct()
                .ToList();

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

            // Season-aligned floor. The row label (season) always comes from this
            // date, never from the final expiry, so a January purchase whose paid
            // year runs into the next calendar year is still labeled with the
            // season it was bought in.
            var seasonEnd = SeasonPassExpiryCalculator.GetNextExpiry(BaseProductKey, null, purchasedAt);
            var season = seasonEnd.Year - 1;

            // Access runs to the end of the paid period when the store reports one
            // that is later than the floor.
            var expiry = seasonEnd;
            if (evt.ExpirationAtMs is { } expirationMs)
            {
                var storeExpiry = DateTimeOffset.FromUnixTimeMilliseconds(expirationMs).UtcDateTime;
                if (storeExpiry > expiry)
                    expiry = storeExpiry;
            }

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
