using System.Text.Json;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Persists user-followed game matchups (team pair) to local app settings.
    /// A matchup is stored as a canonical "lowId:highId" string so order doesn't matter.
    /// </summary>
    public class PersonalGameService
    {
        private const string StorageKey = "personal_followed_games";

        private readonly HashSet<string> _followed = new();

        public event Action<string, bool>? GameFollowChanged;

        public PersonalGameService()
        {
            Load();
        }

        // ── Public API ────────────────────────────────────────────────────

        public bool IsFollowed(int team1Id, int team2Id)
            => _followed.Contains(Key(team1Id, team2Id));

        public void Follow(int team1Id, int team2Id)
        {
            var key = Key(team1Id, team2Id);
            if (_followed.Add(key))
            {
                Save();
                GameFollowChanged?.Invoke(key, true);
            }
        }

        public void Unfollow(int team1Id, int team2Id)
        {
            var key = Key(team1Id, team2Id);
            if (_followed.Remove(key))
            {
                Save();
                GameFollowChanged?.Invoke(key, false);
            }
        }

        public void Toggle(int team1Id, int team2Id)
        {
            if (IsFollowed(team1Id, team2Id))
                Unfollow(team1Id, team2Id);
            else
                Follow(team1Id, team2Id);
        }

        /// <summary>Returns all followed matchup keys as "lowId:highId" pairs.</summary>
        public IReadOnlyCollection<string> GetAll() => _followed;

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

        // ── Persistence ───────────────────────────────────────────────────

        private void Load()
        {
            try
            {
                var json = Preferences.Get(StorageKey, null);
                if (!string.IsNullOrEmpty(json))
                {
                    var list = JsonSerializer.Deserialize<List<string>>(json);
                    if (list != null)
                        foreach (var k in list)
                            _followed.Add(k);
                }
            }
            catch { /* start fresh if corrupt */ }
        }

        private void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(_followed.ToList());
                Preferences.Set(StorageKey, json);
            }
            catch { }
        }
    }
}
