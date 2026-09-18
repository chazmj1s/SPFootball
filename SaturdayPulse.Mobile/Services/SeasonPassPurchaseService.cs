using Maui.RevenueCat.InAppBilling.Enums;
using Maui.RevenueCat.InAppBilling.Services;
using Microsoft.Extensions.Logging;

namespace SaturdayPulse.Services
{
    public enum SeasonPassPurchaseStatus
    {
        Purchased,
        Cancelled,
        Pending,
        Unavailable,
        NothingToRestore,
        Failed
    }

    /// <summary>Outcome of a Season Pass purchase or restore attempt. Message is
    /// safe to show to the user as-is.</summary>
    public sealed record SeasonPassPurchaseResult(SeasonPassPurchaseStatus Status, string Message);

    /// <summary>
    /// Thin wrapper over IRevenueCatBilling for the annual Season Pass
    /// subscription. Lazily initializes the SDK on first use (the wrapper
    /// requires initialization after app start, not in a constructor, and a
    /// purchase/restore tap is always after start). All runtime failures come
    /// back as a result; these methods do not throw.
    ///
    /// API keys: fill AppleApiKey / GoogleApiKey with the platform public SDK
    /// keys (RevenueCat > Project settings > API keys) once the real store apps
    /// are linked. Until then DEBUG builds fall back to the Test Store key;
    /// RELEASE builds with an empty platform key report purchases as
    /// unavailable rather than shipping the test key.
    /// </summary>
    public sealed class SeasonPassPurchaseService
    {
        private const string TestStoreApiKey = "test_yIOCSaMyXlYEJyFvJddUngPFqXi";
        private const string AppleApiKey = "appl_eyorCHPCWfmAtfVANIhnOFhXOGw";
        private const string GoogleApiKey = "";

        private const string OfferingId = "default";
        private const string PackageId = "$rc_annual";
        private const string EntitlementId = "j1s_sports_season_pass";

        private readonly IRevenueCatBilling _billing;
        private readonly ILogger<SeasonPassPurchaseService> _logger;
        private int _operationInProgress;

        public SeasonPassPurchaseService(
            IRevenueCatBilling billing,
            ILogger<SeasonPassPurchaseService> logger)
        {
            _billing = billing;
            _logger = logger;
        }

        private static string? GetApiKey()
        {
#if ANDROID
            return ResolveKey(GoogleApiKey);
#elif IOS
            return ResolveKey(AppleApiKey);
#else
            return null;
#endif
        }

        private static string? ResolveKey(string platformKey)
        {
            if (!string.IsNullOrEmpty(platformKey)) return platformKey;
#if DEBUG
            return TestStoreApiKey;
#else
            return null;
#endif
        }

        /// <param name="appUserId">
        /// The API's UserProfile.UserId for the logged-in account (JWT sub).
        /// Becomes the RevenueCat App User ID so the server-side webhook can
        /// attribute the purchase to the right UserEntitlement row.
        /// </param>
        public async Task<SeasonPassPurchaseResult> PurchaseSeasonPassAsync(string appUserId)
        {
            if (string.IsNullOrWhiteSpace(appUserId))
            {
                return new SeasonPassPurchaseResult(
                    SeasonPassPurchaseStatus.Failed,
                    "Please log in to purchase the Season Pass.");
            }

            if (Interlocked.CompareExchange(ref _operationInProgress, 1, 0) != 0)
            {
                return new SeasonPassPurchaseResult(
                    SeasonPassPurchaseStatus.Failed,
                    "A purchase is already in progress.");
            }

            try
            {
                var failure = await EnsureIdentifiedAsync(appUserId);
                if (failure != null) return failure;

                var offeringsResult = await _billing.GetOfferings();
                var offerings = offeringsResult.Value;
                if (offeringsResult.IsError || offerings is null || offerings.Count == 0)
                {
                    _logger.LogError(
                        offeringsResult.ErrorException,
                        "RevenueCat GetOfferings failed or returned nothing. Error={Error}",
                        offeringsResult.Error);
                    return new SeasonPassPurchaseResult(
                        SeasonPassPurchaseStatus.Unavailable,
                        "The Season Pass isn't available right now. Please try again later.");
                }

                var offering = offerings.FirstOrDefault(o => o.Identifier == OfferingId)
                               ?? offerings.FirstOrDefault(o => o.IsCurrent);
                var package = offering?.AvailablePackages
                    .FirstOrDefault(p => p.Identifier == PackageId);

                if (package is null)
                {
                    _logger.LogError(
                        "RevenueCat package {PackageId} not found in offering {OfferingId}.",
                        PackageId, OfferingId);
                    return new SeasonPassPurchaseResult(
                        SeasonPassPurchaseStatus.Unavailable,
                        "The Season Pass isn't available right now. Please try again later.");
                }

                var purchase = await _billing.PurchaseProduct(package);

                if (purchase.IsSuccess)
                {
                    var entitled = purchase.Value?.Entitlements
                        .Any(e => e.IsActive && e.Identifier == EntitlementId) ?? false;

                    if (entitled)
                    {
                        _logger.LogInformation(
                            "Season Pass purchase succeeded; entitlement {EntitlementId} active.",
                            EntitlementId);
                        return new SeasonPassPurchaseResult(
                            SeasonPassPurchaseStatus.Purchased,
                            "Purchase complete. Thank you!");
                    }

                    _logger.LogError(
                        "Purchase reported success but entitlement {EntitlementId} is not active.",
                        EntitlementId);
                    return new SeasonPassPurchaseResult(
                        SeasonPassPurchaseStatus.Failed,
                        "Your purchase went through but the Season Pass didn't activate. Please contact support.");
                }

                if (purchase.Error == PurchaseErrorStatus.PurchaseCancelledError)
                {
                    return new SeasonPassPurchaseResult(
                        SeasonPassPurchaseStatus.Cancelled,
                        "Purchase cancelled.");
                }

                if (purchase.Error == PurchaseErrorStatus.PaymentPendingError)
                {
                    return new SeasonPassPurchaseResult(
                        SeasonPassPurchaseStatus.Pending,
                        "Your payment is pending approval. The Season Pass will activate once it completes.");
                }

                _logger.LogError(
                    purchase.ErrorException,
                    "Season Pass purchase failed. Error={Error}",
                    purchase.Error);

                var message = purchase.Error == PurchaseErrorStatus.ProductAlreadyPurchasedError
                    ? "You already own the Season Pass. Try Restore Purchases."
                    : "The purchase couldn't be completed. You were not charged. Please try again.";

                return new SeasonPassPurchaseResult(SeasonPassPurchaseStatus.Failed, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during Season Pass purchase.");
                return new SeasonPassPurchaseResult(
                    SeasonPassPurchaseStatus.Failed,
                    "The purchase couldn't be completed. Please try again.");
            }
            finally
            {
                Interlocked.Exchange(ref _operationInProgress, 0);
            }
        }

        /// <summary>
        /// Restores previous store purchases for the logged-in account
        /// (required by App Store review for auto-renewing subscriptions).
        /// Success means the Season Pass entitlement is active in RevenueCat;
        /// the server-side row is the caller's concern (profile refresh).
        /// </summary>
        public async Task<SeasonPassPurchaseResult> RestoreSeasonPassAsync(string appUserId)
        {
            if (string.IsNullOrWhiteSpace(appUserId))
            {
                return new SeasonPassPurchaseResult(
                    SeasonPassPurchaseStatus.Failed,
                    "Please log in to restore purchases.");
            }

            if (Interlocked.CompareExchange(ref _operationInProgress, 1, 0) != 0)
            {
                return new SeasonPassPurchaseResult(
                    SeasonPassPurchaseStatus.Failed,
                    "Another purchase operation is already in progress.");
            }

            try
            {
                var failure = await EnsureIdentifiedAsync(appUserId);
                if (failure != null) return failure;

                _logger.LogInformation("Restoring purchases for {UserId}.", appUserId);

                // A restore that never completes would otherwise look like the
                // button doing nothing, so cap it.
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var restore = await _billing.RestoreTransactions(timeout.Token);
                if (restore.IsError)
                {
                    _logger.LogError(
                        restore.ErrorException,
                        "RevenueCat RestoreTransactions failed. Error={Error}",
                        restore.Error);

                    var restoreMessage = restore.Error == PurchaseErrorStatus.PurchaseCancelledError
                        ? "Restoring took too long. Check your connection and try again."
                        : "We couldn't restore your purchases. Please try again.";

                    return new SeasonPassPurchaseResult(
                        SeasonPassPurchaseStatus.Failed,
                        restoreMessage);
                }

                var entitled = restore.Value?.Entitlements
                    .Any(e => e.IsActive && e.Identifier == EntitlementId) ?? false;

                return entitled
                    ? new SeasonPassPurchaseResult(
                        SeasonPassPurchaseStatus.Purchased,
                        "Your Season Pass has been restored.")
                    : new SeasonPassPurchaseResult(
                        SeasonPassPurchaseStatus.NothingToRestore,
                        "No active Season Pass purchase was found for this store account.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during Season Pass restore.");
                return new SeasonPassPurchaseResult(
                    SeasonPassPurchaseStatus.Failed,
                    "We couldn't restore your purchases. Please try again.");
            }
            finally
            {
                Interlocked.Exchange(ref _operationInProgress, 0);
            }
        }

        /// <summary>
        /// Initializes the SDK for this user, or switches the SDK to this user
        /// if it was already initialized for someone else. Returns null on
        /// success, or the failure result to hand back to the caller.
        /// </summary>
        private async Task<SeasonPassPurchaseResult?> EnsureIdentifiedAsync(string appUserId)
        {
            var apiKey = GetApiKey();
            if (apiKey is null)
            {
                return new SeasonPassPurchaseResult(
                    SeasonPassPurchaseStatus.Unavailable,
                    "In-app purchases aren't available on this platform.");
            }

            if (!_billing.IsInitialized())
            {
                // Custom App User ID at init: RevenueCat never creates an
                // anonymous ($RCAnonymousID) customer for this user.
                _billing.Initialize(apiKey, appUserId);
                return null;
            }

            if (!string.Equals(_billing.GetAppUserId(), appUserId, StringComparison.Ordinal))
            {
                // Already initialized this session for a different (or
                // anonymous) user, e.g. after a logout/login as someone else.
                var loginResult = await _billing.Login(appUserId);
                if (loginResult.IsError)
                {
                    _logger.LogError(
                        loginResult.ErrorException,
                        "RevenueCat Login failed. Error={Error}",
                        loginResult.Error);
                    return new SeasonPassPurchaseResult(
                        SeasonPassPurchaseStatus.Failed,
                        "We couldn't verify your account with the store. Please try again.");
                }
            }

            return null;
        }
    }
}
