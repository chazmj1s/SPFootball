using Auth0.OidcClient;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Wraps the singleton Auth0Client (registered in MauiProgram.cs) with
    /// SecureStorage-backed token persistence. This is the only class that
    /// should call Auth0Client.LoginAsync/LogoutAsync directly.
    ///
    /// Token storage is credential-shaped, so SecureStorage, not Preferences
    /// (Preferences is plaintext). HasAccount/StayLoggedIn are plain booleans,
    /// not credentials, so Preferences is fine for those.
    ///
    /// By design there is NO forced login anywhere in the app. Login only
    /// happens when the person taps the Login/Create Account button in
    /// Settings, or (indirectly) taps Season Pass while logged out. Everything
    /// else keeps working via the X-User-Id fallback in UserApiService,
    /// exactly as before.
    ///
    /// NOTE: this is the login/logout half of item 3 on the handoff list.
    /// Windows support (item 1) is a separate, still-open piece — GH #2702
    /// means WebAuthenticator may need extra plumbing on Windows/MSIX that
    /// hasn't been added here yet.
    /// </summary>
    public class AuthService
    {
        private const string AccessTokenKey  = "auth0_access_token";
        private const string RefreshTokenKey = "auth0_refresh_token";
        private const string ExpiresAtKey    = "auth0_expires_at";
        private const string HasAccountKey   = "auth0_has_account";
        private const string StayLoggedInKey = "auth0_stay_logged_in";

        private readonly Auth0Client _auth0Client;

        public AuthService(Auth0Client auth0Client)
        {
            _auth0Client = auth0Client;
        }

        /// <summary>
        /// True once a login has ever succeeded on this device — drives the
        /// Login vs. Create Account button label in Settings. Set on first
        /// successful LoginAsync, never cleared by Logout (logging out just
        /// ends the session; it doesn't erase "this device has an account").
        /// </summary>
        public bool HasAccount
        {
            get => Preferences.Default.Get(HasAccountKey, false);
            private set => Preferences.Default.Set(HasAccountKey, value);
        }

        /// <summary>
        /// User-facing "Stay Logged In" toggle (Settings > User Profile),
        /// default ON. This does NOT gate a login screen — there isn't one.
        /// It only gates whether GetAccessTokenAsync honors a stored/refreshed
        /// session: turn it off and the app behaves as logged-out (falls back
        /// to X-User-Id) without deleting the underlying tokens; turn it back
        /// on and the existing session resumes with no re-login required.
        /// </summary>
        public bool StayLoggedIn
        {
            get => Preferences.Default.Get(StayLoggedInKey, true);
            set => Preferences.Default.Set(StayLoggedInKey, value);
        }

        /// <summary>
        /// Launches the Auth0 Universal Login page via WebAuthenticator and
        /// persists the resulting tokens on success.
        /// </summary>
        /// <param name="isSignup">
        /// When true, hints Auth0's hosted page to open on the signup tab
        /// instead of login (screen_hint=signup) — used for the "Create
        /// Account" button state. Auth0ClientBase.LoginAsync takes a plain
        /// `object extraParameters` and builds the internal LoginRequest/
        /// Parameters wrapper itself — an anonymous object with the query
        /// param name as the property name is the documented shape (see
        /// Auth0's own MAUI quickstart), no need to construct that wrapper
        /// by hand.
        /// </param>
        public async Task<bool> LoginAsync(bool isSignup = false)
        {
            var result = isSignup
                ? await _auth0Client.LoginAsync(new { screen_hint = "signup" })
                : await _auth0Client.LoginAsync();

            if (result.IsError)
            {
                System.Diagnostics.Debug.WriteLine($"[Auth] Login failed: {result.Error}");
                return false;
            }

            await SecureStorage.Default.SetAsync(AccessTokenKey, result.AccessToken);
            await SecureStorage.Default.SetAsync(
                ExpiresAtKey,
                DateTimeOffset.UtcNow.Add(result.AccessTokenExpiration - DateTime.UtcNow).ToString("O"));

            if (!string.IsNullOrEmpty(result.RefreshToken))
                await SecureStorage.Default.SetAsync(RefreshTokenKey, result.RefreshToken);

            HasAccount = true;
            return true;
        }

        public async Task LogoutAsync()
        {
            await _auth0Client.LogoutAsync();
            SecureStorage.Default.Remove(AccessTokenKey);
            SecureStorage.Default.Remove(RefreshTokenKey);
            SecureStorage.Default.Remove(ExpiresAtKey);
        }

        /// <summary>
        /// Returns a valid access token, refreshing silently if expired and a
        /// refresh token is available. Returns null if there's no session, OR
        /// if StayLoggedIn is off — callers (UserApiService) treat null as
        /// "not logged in right now" and fall back to the legacy X-User-Id
        /// header, exactly as they do today for someone who's never logged in.
        /// </summary>
        public async Task<string?> GetAccessTokenAsync()
        {
            if (!StayLoggedIn) return null;

            var token = await SecureStorage.Default.GetAsync(AccessTokenKey);
            if (string.IsNullOrEmpty(token)) return null;

            var expiresAtRaw = await SecureStorage.Default.GetAsync(ExpiresAtKey);
            if (DateTimeOffset.TryParse(expiresAtRaw, out var expiresAt) &&
                expiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return token;
            }

            // Expired (or expiry unreadable) — try a silent refresh.
            var refreshToken = await SecureStorage.Default.GetAsync(RefreshTokenKey);
            if (string.IsNullOrEmpty(refreshToken)) return null;

            var refreshed = await _auth0Client.RefreshTokenAsync(refreshToken);
            if (refreshed.IsError)
            {
                System.Diagnostics.Debug.WriteLine($"[Auth] Silent refresh failed: {refreshed.Error}");
                return null;
            }

            await SecureStorage.Default.SetAsync(AccessTokenKey, refreshed.AccessToken);
            await SecureStorage.Default.SetAsync(
                ExpiresAtKey,
                DateTimeOffset.UtcNow.Add(refreshed.AccessTokenExpiration - DateTime.UtcNow).ToString("O"));
            if (!string.IsNullOrEmpty(refreshed.RefreshToken))
                await SecureStorage.Default.SetAsync(RefreshTokenKey, refreshed.RefreshToken);

            return refreshed.AccessToken;
        }

        /// <summary>
        /// True if there's a currently-usable session — false whenever
        /// StayLoggedIn is off, even if a valid token is sitting in storage
        /// (that's the point: off means "act logged out").
        /// </summary>
        public async Task<bool> IsAuthenticatedAsync() => await GetAccessTokenAsync() is not null;
    }
}
