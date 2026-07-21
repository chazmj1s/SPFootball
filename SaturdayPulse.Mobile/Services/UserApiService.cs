using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Service for calling the NCAA Power Ratings User API (api/user/...).
    ///
    /// Auth: sends a real "Authorization: Bearer {token}" header when
    /// AuthService reports a logged-in session, otherwise falls back to the
    /// legacy X-User-Id header. This mirrors HttpContextUserExtensions.cs on
    /// the API side, which is dual-mode by design (JWT sub claim first,
    /// X-User-Id fallback) — see session-handoff-2026-07-19.md.
    ///
    /// The fallback path (LocalUserId / ForcedDevUserId / X-User-Id) is
    /// still transitional plumbing. Per the handoff's "not done yet" list,
    /// it stays until Windows login (item 1) also works — don't delete it
    /// yet even though iOS/mobile login is now wired.
    /// </summary>
    public class UserApiService
    {
        private const string LocalUserIdKey = "LocalUserId";

        // TEMPORARY: forces every launch onto the seeded dev profile
        // (6c753c66-a2e2-43f2-91ac-efdee1cbec90 — Handle Chazmj1sTx) instead
        // of whatever random GUID happens to be sitting in this device's
        // Preferences. Unblocks testing against real data pre-Auth0. Delete
        // this override (and the two lines that use it below) once Auth0
        // login is wired on every target platform (Windows still pending —
        // see handoff item 1) — that's the real fix for "how does it know
        // who I am."
        private static readonly Guid ForcedDevUserId =
            Guid.Parse("6c753c66-a2e2-43f2-91ac-efdee1cbec90");

        private readonly HttpClient _httpClient;
        private readonly AuthService _authService;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private Guid? _cachedUserId;

        public UserApiService(HttpClient httpClient, AuthService authService)
        {
            _httpClient = httpClient;
            _authService = authService;
        }

        /// <summary>
        /// The device's local UserId. Generated once and persisted to
        /// Preferences on first access; stable for the life of the install.
        /// Only used as the fallback identity when there's no Auth0 session.
        /// </summary>
        public Guid LocalUserId
        {
            get
            {
                if (_cachedUserId.HasValue) return _cachedUserId.Value;

                // TEMPORARY override — see ForcedDevUserId above. Also
                // writes it to Preferences so anything that reads the raw
                // pref directly (there shouldn't be anything, but just in
                // case) sees the same value.
                Preferences.Default.Set(LocalUserIdKey, ForcedDevUserId.ToString());
                _cachedUserId = ForcedDevUserId;
                return ForcedDevUserId;
            }
        }

        // ── Profile ──────────────────────────────────────────────────────

        /// <summary>
        /// GET /user/me. Server auto-provisions a UserProfile row on first
        /// contact for a UserId it hasn't seen.
        /// </summary>
        public async Task<UserProfileDto?> GetMeAsync()
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "user/me");
                await AttachAuthAsync(request);

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[UserAPI] GetMe failed: {response.StatusCode}");
                    return null;
                }

                return await response.Content.ReadFromJsonAsync<UserProfileDto>(_jsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserAPI] Error GetMe: {ex.Message}");
                return null;
            }
        }

        /// <summary>PATCH /user/me/primary-team. Null clears the primary team.</summary>
        public async Task<bool> SetPrimaryTeamAsync(int? teamId)
        {
            try
            {
                // ASSUMPTION: body shape is { "teamId": int? }. Confirm against
                // UserController's actual DTO once shared — adjust field name
                // here if it doesn't match.
                using var request = new HttpRequestMessage(HttpMethod.Patch, "user/me/primary-team")
                {
                    Content = JsonContent.Create(new { teamId })
                };
                await AttachAuthAsync(request);

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    System.Diagnostics.Debug.WriteLine($"[UserAPI] SetPrimaryTeam failed: {response.StatusCode}");

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserAPI] Error SetPrimaryTeam: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// PATCH /user/me/handle. ASSUMPTION: body shape is { "handle": string } —
        /// by analogy with UpdatePrimaryTeamRequest's { "teamId": ... } shape.
        /// UpdateHandleRequest.cs wasn't available to confirm the exact field
        /// name; adjust here if the server returns 400 on a well-formed handle.
        /// </summary>
        public async Task<bool> UpdateHandleAsync(string handle)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Patch, "user/me/handle")
                {
                    Content = JsonContent.Create(new { handle })
                };
                await AttachAuthAsync(request);

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    System.Diagnostics.Debug.WriteLine($"[UserAPI] UpdateHandle failed: {response.StatusCode}");

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserAPI] Error UpdateHandle: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// PATCH /user/me/email. ASSUMPTION: body shape is { "email": string } —
        /// same reasoning as UpdateHandleAsync above.
        /// </summary>
        public async Task<bool> UpdateEmailAsync(string email)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Patch, "user/me/email")
                {
                    Content = JsonContent.Create(new { email })
                };
                await AttachAuthAsync(request);

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    System.Diagnostics.Debug.WriteLine($"[UserAPI] UpdateEmail failed: {response.StatusCode}");

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserAPI] Error UpdateEmail: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// PATCH /user/me/phone. ASSUMPTION: body shape is
        /// { "phoneNumber": string, "marketingSmsConsent": bool } — field
        /// names inferred from UserController's
        /// UpdatePhoneAsync(userId, request.PhoneNumber, request.MarketingSmsConsent, token)
        /// call signature, which is about as solid as an assumption gets
        /// without the actual UpdatePhoneRequest.cs file.
        /// </summary>
        public async Task<bool> UpdatePhoneAsync(string phoneNumber, bool marketingSmsConsent)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Patch, "user/me/phone")
                {
                    Content = JsonContent.Create(new { phoneNumber, marketingSmsConsent })
                };
                await AttachAuthAsync(request);

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    System.Diagnostics.Debug.WriteLine($"[UserAPI] UpdatePhone failed: {response.StatusCode}");

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserAPI] Error UpdatePhone: {ex.Message}");
                return false;
            }
        }

        // ── Followed teams ──────────────────────────────────────────────

        /// <summary>GET /user/me/followed-teams. ASSUMPTION: returns a flat List&lt;int&gt; of team ids.</summary>
        public async Task<List<int>?> GetFollowedTeamsAsync()
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "user/me/followed-teams");
                await AttachAuthAsync(request);

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[UserAPI] GetFollowedTeams failed: {response.StatusCode}");
                    return null;
                }

                return await response.Content.ReadFromJsonAsync<List<int>>(_jsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserAPI] Error GetFollowedTeams: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> FollowTeamAsync(int teamId)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Put, $"user/me/followed-teams/{teamId}");
                await AttachAuthAsync(request);

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    System.Diagnostics.Debug.WriteLine($"[UserAPI] FollowTeam({teamId}) failed: {response.StatusCode}");

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserAPI] Error FollowTeam({teamId}): {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UnfollowTeamAsync(int teamId)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, $"user/me/followed-teams/{teamId}");
                await AttachAuthAsync(request);

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    System.Diagnostics.Debug.WriteLine($"[UserAPI] UnfollowTeam({teamId}) failed: {response.StatusCode}");

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserAPI] Error UnfollowTeam({teamId}): {ex.Message}");
                return false;
            }
        }

        // ── Followed games (team-pair matchups) ─────────────────────────

        /// <summary>GET /user/me/followed-games. ASSUMPTION: returns List&lt;FollowedGamePairDto&gt;.</summary>
        public async Task<List<FollowedGamePairDto>?> GetFollowedGamesAsync()
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "user/me/followed-games");
                await AttachAuthAsync(request);

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[UserAPI] GetFollowedGames failed: {response.StatusCode}");
                    return null;
                }

                return await response.Content.ReadFromJsonAsync<List<FollowedGamePairDto>>(_jsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserAPI] Error GetFollowedGames: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> FollowGameAsync(int team1Id, int team2Id)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Put, $"user/me/followed-games?team1Id={team1Id}&team2Id={team2Id}");
                await AttachAuthAsync(request);

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    System.Diagnostics.Debug.WriteLine($"[UserAPI] FollowGame({team1Id},{team2Id}) failed: {response.StatusCode}");

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserAPI] Error FollowGame({team1Id},{team2Id}): {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UnfollowGameAsync(int team1Id, int team2Id)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Delete, $"user/me/followed-games?team1Id={team1Id}&team2Id={team2Id}");
                await AttachAuthAsync(request);

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    System.Diagnostics.Debug.WriteLine($"[UserAPI] UnfollowGame({team1Id},{team2Id}) failed: {response.StatusCode}");

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserAPI] Error UnfollowGame({team1Id},{team2Id}): {ex.Message}");
                return false;
            }
        }

        // ── Auth plumbing ──────────────────────────────────────────────

        /// <summary>
        /// Attaches auth to the outgoing request: a real Bearer token if
        /// AuthService reports a logged-in Auth0 session, otherwise the
        /// legacy X-User-Id header. Safe to call unconditionally — the API's
        /// HttpContextUserExtensions checks the JWT sub claim first and
        /// falls back to X-User-Id itself, so both paths work today.
        /// </summary>
        private async Task AttachAuthAsync(HttpRequestMessage request)
        {
            var token = await _authService.GetAccessTokenAsync();
            if (token is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return;
            }

            request.Headers.Remove("X-User-Id");
            request.Headers.Add("X-User-Id", LocalUserId.ToString());
        }
    }

    // ── DTOs ─────────────────────────────────────────────────────────────
    // Mirrors UserProfile / the followed-games list shape. Move into
    // SaturdayPulse.Models if that's where the rest of the DTOs live —
    // kept local here since UserController.cs wasn't available to confirm.

    public class UserProfileDto
    {
        public Guid UserId { get; set; }
        public string Handle { get; set; } = string.Empty;
        public DateTime? HandleChangedAt { get; set; }
        public int? PrimaryTeamId { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsSynced { get; set; }

        // ASSUMPTION: GetMe() merges UserContactInfo into the same response
        // (the controller's class summary is "Profile, contact info, and
        // follow management"). If it doesn't, these just deserialize as
        // null/false and Email/Phone show blank in Settings until a
        // dedicated contact-info fetch is added — confirm against
        // UserProfileService.cs when you get a chance.
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public bool? MarketingSmsConsent { get; set; }
    }

    public class FollowedGamePairDto
    {
        public int Team1Id { get; set; }
        public int Team2Id { get; set; }
    }
}
