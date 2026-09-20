using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Singleton service that owns Season Pass / admin entitlement state for
    /// the whole app — same shape as FollowService: an in-memory cache other
    /// ViewModels read synchronously (IsLoggedIn/HasSeasonPass/IsAdmin),
    /// updated whenever a fresh UserProfileDto comes back from the server,
    /// and an EntitlementChanged event so PowerRankingsViewModel/
    /// ScheduleViewModel/MyTeamsViewModel/PostseasonViewModel/
    /// SandboxViewModel can react to entitlement changes without depending
    /// on SettingsViewModel directly (2026-07-25 design decision).
    ///
    /// This does NOT independently poll the API. SettingsViewModel already
    /// owns the login / create-account / dev-toggle / passive-startup-load
    /// flows and has the UserProfileDto in hand at the end of each — it
    /// just also calls ApplyProfile here so every other ViewModel finds out.
    /// Logout calls Clear().
    ///
    /// EnsureLoggedInForPurchaseAsync holds the Season Pass "log in first"
    /// check that used to live entirely inside SettingsViewModel.SeasonPassCommand.
    /// Pulling it out here means the Settings button and the new gated
    /// Details paywall message (MyTeamsViewModel) share the exact same
    /// login-check logic. PurchaseSeasonPassAsync is the matching single
    /// entry point for the store purchase itself (RevenueCat).
    /// </summary>
    public class EntitlementService
    {
        private readonly AuthService _authService;
        private readonly UserApiService _userApi;
        private readonly SeasonPassPurchaseService _purchaseService;

        private bool _isLoggedIn;
        private bool _hasSeasonPass;
        private bool _isAdmin;
        private string? _userId;

        public bool IsLoggedIn => _isLoggedIn;
        public bool HasSeasonPass => _hasSeasonPass;
        public bool IsAdmin => _isAdmin;

        /// <summary>The API-side UserId (JWT sub) of the logged-in account; null when logged out.</summary>
        public string? UserId => _userId;

        /// <summary>Fires whenever IsLoggedIn, HasSeasonPass, or IsAdmin changes.</summary>
        public event Action? EntitlementChanged;

        public EntitlementService(
            AuthService authService,
            UserApiService userApi,
            SeasonPassPurchaseService purchaseService)
        {
            _authService = authService;
            _userApi = userApi;
            _purchaseService = purchaseService;
        }

        /// <summary>
        /// Call whenever a fresh profile comes back from the server — login,
        /// create account, passive startup fetch, or the admin dev-entitlement
        /// toggle. Safe to call repeatedly; only raises EntitlementChanged if
        /// something actually changed.
        /// </summary>
        public void ApplyProfile(UserProfileDto profile)
        {
            var changed = !_isLoggedIn
                || _hasSeasonPass != profile.IsEntitled
                || _isAdmin != profile.IsAdmin;

            _isLoggedIn = true;
            _hasSeasonPass = profile.IsEntitled;
            _isAdmin = profile.IsAdmin;
            _userId = string.IsNullOrWhiteSpace(profile.UserId) ? null : profile.UserId;

            if (changed) EntitlementChanged?.Invoke();
        }

        /// <summary>Call on logout.</summary>
        public void Clear()
        {
            var changed = _isLoggedIn || _hasSeasonPass || _isAdmin;

            _isLoggedIn = false;
            _hasSeasonPass = false;
            _isAdmin = false;
            _userId = null;

            if (changed) EntitlementChanged?.Invoke();
        }

        /// <summary>
        /// Season Pass purchase entry point. If nobody's logged in, offers
        /// to log in first (same prompt SettingsViewModel's original
        /// SeasonPassCommand showed); returns whether the caller can proceed
        /// to the purchase flow (PurchaseSeasonPassAsync), plus the
        /// freshly-fetched profile if a login just happened — callers that
        /// keep their own local copy of profile fields (SettingsViewModel)
        /// should apply FreshProfile themselves; callers that only read
        /// through this service (MyTeamsViewModel) don't need to do anything
        /// else, since ApplyProfile was already called internally.
        /// </summary>
        public async Task<SeasonPassLoginResult> EnsureLoggedInForPurchaseAsync()
        {
            if (_isLoggedIn) return new SeasonPassLoginResult(true, null);

            var proceed = await Shell.Current.DisplayAlert(
                "Season Pass",
                "You'll need an account to purchase a Season Pass.",
                "Log In", "Cancel");
            if (!proceed) return new SeasonPassLoginResult(false, null);

            var authOk = await _authService.LoginAsync(isSignup: false);
            if (!authOk) return new SeasonPassLoginResult(false, null);

            // Same first-login handling as SettingsViewModel.TryLoginAsync: Auth0's
            // hosted page can't tell a first-time Apple/Google login from a
            // returning one, so a login with no profile creates one (the server's
            // email-uniqueness check still blocks duplicates).
            var profile = await _userApi.GetMeAsync();
            if (profile == null)
            {
                var outcome = await _userApi.CreateAccountAsync(_authService.LastLoginEmail);

                if (outcome.IsSuccess && outcome.Profile != null)
                {
                    profile = outcome.Profile;
                }
                else
                {
                    // GetMeAsync returns null for any failure, not only a 404, so a
                    // conflict can mean the account exists and the first lookup
                    // failed. Re-check once before treating it as a real conflict.
                    profile = await _userApi.GetMeAsync();

                    if (profile == null)
                    {
                        if (outcome.IsConflict)
                        {
                            await _authService.LogoutAsync();
                            var reason = (outcome.ConflictMessage ?? "That account can't be used.").Trim('"');
                            await Shell.Current.DisplayAlert(
                                "Season Pass",
                                $"{reason} Log in with the sign-in method you used originally.",
                                "OK");
                        }
                        else
                        {
                            await Shell.Current.DisplayAlert(
                                "Season Pass",
                                "Couldn't reach the server — check your connection and try again.",
                                "OK");
                        }

                        return new SeasonPassLoginResult(false, null);
                    }
                }
            }

            ApplyProfile(profile);
            return new SeasonPassLoginResult(true, profile);
        }

        /// <summary>
        /// Runs the store purchase for the annual Season Pass. Call only
        /// after EnsureLoggedInForPurchaseAsync returned CanProceed = true.
        /// Does not throw; failures are reported in the result.
        /// </summary>
        public Task<SeasonPassPurchaseResult> PurchaseSeasonPassAsync()
            => _purchaseService.PurchaseSeasonPassAsync(_userId ?? string.Empty);

        /// <summary>
        /// Restores previous store purchases for the logged-in account.
        /// Does not throw; failures are reported in the result.
        /// </summary>
        public Task<SeasonPassPurchaseResult> RestoreSeasonPassAsync()
            => _purchaseService.RestoreSeasonPassAsync(_userId ?? string.Empty);
    }

    /// <summary>Result of EnsureLoggedInForPurchaseAsync — CanProceed tells the
    /// caller whether to continue to the purchase flow; FreshProfile is
    /// non-null only when a new login happened during this call.</summary>
    public readonly record struct SeasonPassLoginResult(bool CanProceed, UserProfileDto? FreshProfile);
}
