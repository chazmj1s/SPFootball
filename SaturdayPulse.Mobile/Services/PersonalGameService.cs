namespace SaturdayPulse.Services
{
    /// <summary>
    /// Owns user-followed game matchups (team pair) across the app.
    /// A matchup is cached in-memory as a canonical "lowId:highId" string
    /// (order-independent), matching FollowedGame's composite PK shape.
    ///
    /// Backed by api/user/me/followed-games. IsFavorited() stays
    /// synchronous — it reads an in-memory cache populated by
    /// InitializeAsync() at startup. Follow()/Unfollow()/Toggle() update
    /// the cache immediately and fire off the API write without awaiting
    /// it: a lost write on a follow toggle is a non-event.
    /// </summary>
    public class PersonalGameService
    {
        private readonly UserApiService _userApi;
        private readonly HashSet<string> _favorited = new();

        public event Action<string, bool>? GameFavoritedChange;

        public PersonalGameService(UserApiService userApi)
        {
            _userApi = userApi;
        }

        /// <summary>
        /// Populates the favorited-matchup cache from the server. Call once
        /// at app startup, before any page reads IsFavorited() for real data.
        /// Safe to call again to refresh.
        /// </summary>
        public async Task InitializeAsync()
        {
            _favorited.Clear();

            var followed = await _userApi.GetFollowedGamesAsync();
            if (followed != null)
            {
                foreach (var pair in followed)
                    _favorited.Add(Key(pair.Team1Id, pair.Team2Id));
            }
        }

        // ── Public API ────────────────────────────────────────────────────

        public bool IsFavorited(int team1Id, int team2Id)
            => _favorited.Contains(Key(team1Id, team2Id));

        public void Follow(int team1Id, int team2Id)
        {
            var key = Key(team1Id, team2Id);
            if (_favorited.Add(key))
            {
                GameFavoritedChange?.Invoke(key, true);
                _ = SyncFollowAsync(team1Id, team2Id);
            }
        }

        public void Unfollow(int team1Id, int team2Id)
        {
            var key = Key(team1Id, team2Id);
            if (_favorited.Remove(key))
            {
                GameFavoritedChange?.Invoke(key, false);
                _ = SyncUnfollowAsync(team1Id, team2Id);
            }
        }

        public void Toggle(int team1Id, int team2Id)
        {
            if (IsFavorited(team1Id, team2Id))
                Unfollow(team1Id, team2Id);
            else
                Follow(team1Id, team2Id);
        }

        /// <summary>Returns all favorited matchup keys as "lowId:highId" pairs.</summary>
        public IReadOnlyCollection<string> GetAll() => _favorited;

        /// <summary>Parses a key back into the two team IDs.</summary>
        public static (int, int) ParseKey(string key)
        {
            var parts = key.Split(':');
            return (int.Parse(parts[0]), int.Parse(parts[1]));
        }

        // ── Canonical key — order independent ────────────────────────────

        public static string Key(int team1Id, int team2Id)
        {
            var lo = Math.Min(team1Id, team2Id);
            var hi = Math.Max(team1Id, team2Id);
            return $"{lo}:{hi}";
        }

        // ── Fire-and-forget sync ─────────────────────────────────────────

        private async Task SyncFollowAsync(int team1Id, int team2Id)
        {
            try
            {
                var ok = await _userApi.FollowGameAsync(team1Id, team2Id);
                if (!ok)
                    System.Diagnostics.Debug.WriteLine($"[PersonalGameService] Server sync failed for {team1Id}v{team2Id}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PersonalGameService] Follow sync error for {team1Id}v{team2Id}: {ex.Message}");
            }
        }

        private async Task SyncUnfollowAsync(int team1Id, int team2Id)
        {
            try
            {
                var ok = await _userApi.UnfollowGameAsync(team1Id, team2Id);
                if (!ok)
                    System.Diagnostics.Debug.WriteLine($"[PersonalGameService] Server unfollow sync failed for {team1Id}v{team2Id}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PersonalGameService] Unfollow sync error for {team1Id}v{team2Id}: {ex.Message}");
            }
        }
    }
}
