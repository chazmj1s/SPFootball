namespace SaturdayPulse.Services
{
    /// <summary>
    /// Singleton service that owns follow state for all teams across the app.
    /// All ViewModels subscribe to TeamFollowChanged to react to toggles.
    ///
    /// Backed by api/user/me/followed-teams and UserProfile.PrimaryTeamId.
    /// IsFollowed()/GetPrimaryTeamId() stay synchronous — they read an
    /// in-memory cache populated by InitializeAsync() at startup — so
    /// existing XAML bindings and FollowIcon controls are untouched.
    /// Toggle()/SetPrimaryTeam() update the cache immediately and fire off
    /// the API write without awaiting it: a lost write on a follow toggle
    /// is a non-event.
    /// </summary>
    public class FollowService
    {
        private readonly UserApiService _userApi;
        private readonly HashSet<int> _followedIds = new();
        private int? _primaryTeamId;

        // Fires whenever a team's follow state changes: (TeamID, IsFollowed)
        public event Action<int, bool>? TeamFollowChanged;

        // Fires whenever the primary team changes. Null = primary team cleared.
        public event Action<int?>? PrimaryTeamChanged;

        public FollowService(UserApiService userApi)
        {
            _userApi = userApi;
        }

        /// <summary>
        /// Populates the follow cache and primary team from the server.
        /// Call once at app startup, before any page reads IsFollowed()/
        /// GetPrimaryTeamId() for real data. Safe to call again to refresh.
        /// </summary>
        public async Task InitializeAsync()
        {
            var profile = await _userApi.GetMeAsync();
            _primaryTeamId = profile?.PrimaryTeamId;

            var followed = await _userApi.GetFollowedTeamsAsync();
            _followedIds.Clear();
            if (followed != null)
            {
                foreach (var id in followed)
                    _followedIds.Add(id);
            }

            // Nothing raised PrimaryTeamChanged until just now, so anything
            // that only refreshes off that event (SettingsViewModel's
            // DefaultTeamDisplay, MyTeamsViewModel's chip list) was stuck
            // showing whatever it read before this async fetch resolved —
            // typically "None"/empty. Reusing the existing event rather than
            // adding a new one since every current subscriber already
            // handles it correctly.
            PrimaryTeamChanged?.Invoke(_primaryTeamId);
        }

        public bool IsFollowed(int teamId) => _followedIds.Contains(teamId);

        public void Toggle(int teamId)
        {
            bool nowFollowed;
            if (_followedIds.Contains(teamId))
            {
                _followedIds.Remove(teamId);
                nowFollowed = false;
            }
            else
            {
                _followedIds.Add(teamId);
                nowFollowed = true;
            }

            TeamFollowChanged?.Invoke(teamId, nowFollowed);

            // Fire-and-forget by design — see class remarks.
            _ = SyncTeamFollowAsync(teamId, nowFollowed);
        }

        public HashSet<int> GetFollowedIds() => new(_followedIds);

        /// <summary>
        /// Primary team is intentionally independent of the followed set —
        /// a team can be primary without being followed. Sourced from
        /// UserProfile.PrimaryTeamId (single source of truth as of the
        /// user-profile migration — no more local Preferences copy).
        /// </summary>
        public int? GetPrimaryTeamId() => _primaryTeamId;

        public void SetPrimaryTeam(int? teamId)
        {
            _primaryTeamId = teamId;
            PrimaryTeamChanged?.Invoke(teamId);

            // Fire-and-forget by design — see class remarks.
            _ = SyncPrimaryTeamAsync(teamId);
        }

        private async Task SyncTeamFollowAsync(int teamId, bool nowFollowed)
        {
            try
            {
                var ok = nowFollowed
                    ? await _userApi.FollowTeamAsync(teamId)
                    : await _userApi.UnfollowTeamAsync(teamId);

                if (!ok)
                    System.Diagnostics.Debug.WriteLine($"[FollowService] Server sync failed for team {teamId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FollowService] Sync error for team {teamId}: {ex.Message}");
            }
        }

        private async Task SyncPrimaryTeamAsync(int? teamId)
        {
            try
            {
                var ok = await _userApi.SetPrimaryTeamAsync(teamId);
                if (!ok)
                    System.Diagnostics.Debug.WriteLine($"[FollowService] Server sync failed for primary team {teamId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FollowService] Primary team sync error: {ex.Message}");
            }
        }
    }
}
