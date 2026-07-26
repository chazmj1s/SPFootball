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
    /// login-check logic — when Stripe gets wired up for real, it only
    /// needs to change in this one place.
    /// </summary>
    public class EntitlementService
    {
        private readonly AuthService _authService;
        private readonly UserApiService _userApi;

        private bool _isLoggedIn;
        private bool _hasSeasonPass;
        private bool _isAdmin;

        public bool IsLoggedIn => _isLoggedIn;
        public bool HasSeasonPass => _hasSeasonPass;
        public bool IsAdmin => _isAdmin;

        /// <summary>Fires whenever IsLoggedIn, HasSeasonPass, or IsAdmin changes.</summary>
        public event Action? EntitlementChanged;

        public EntitlementService(AuthService authService, UserApiService userApi)
        {
            _authService = authService;
            _userApi = userApi;
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

            if (changed) EntitlementChanged?.Invoke();
        }

        /// <summary>Call on logout.</summary>
        public void Clear()
        {
            var changed = _isLoggedIn || _hasSeasonPass || _isAdmin;

            _isLoggedIn = false;
            _hasSeasonPass = false;
            _isAdmin = false;

            if (changed) EntitlementChanged?.Invoke();
        }

        /// <summary>
        /// Season Pass purchase entry point. If nobody's logged in, offers
        /// to log in first (same prompt SettingsViewModel's original
        /// SeasonPassCommand showed); returns whether the caller can proceed
        /// to the (not-yet-built) purchase flow, plus the freshly-fetched
        /// profile if a login just happened — callers that keep their own
        /// local copy of profile fields (SettingsViewModel) should apply
        /// FreshProfile themselves; callers that only read through this
        /// service (MyTeamsViewModel) don't need to do anything else, since
        /// ApplyProfile was already called internally.
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

            var profile = await _userApi.GetMeAsync();
            if (profile == null) return new SeasonPassLoginResult(false, null);

            ApplyProfile(profile);
            return new SeasonPassLoginResult(true, profile);
        }
    }

    /// <summary>Result of EnsureLoggedInForPurchaseAsync — CanProceed tells the
    /// caller whether to continue to the purchase flow; FreshProfile is
    /// non-null only when a new login happened during this call.</summary>
    public readonly record struct SeasonPassLoginResult(bool CanProceed, UserProfileDto? FreshProfile);
}
