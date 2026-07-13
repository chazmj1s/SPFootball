using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Singleton service that owns follow state for all teams across the app.
    /// All ViewModels subscribe to TeamFollowChanged to react to toggles.
    /// </summary>
    public class FollowService
    {
        private const string FollowedTeamsKey = "FollowedTeams";
        private const string PrimaryTeamKey = "PrimaryTeamId";
        private readonly HashSet<int> _followedIds;

        // Fires whenever a team's follow state changes: (TeamID, IsFollowed)
        public event Action<int, bool>? TeamFollowChanged;

        // Fires whenever the primary team changes. Null = primary team cleared.
        public event Action<int?>? PrimaryTeamChanged;

        public FollowService()
        {
            _followedIds = LoadFromPreferences();
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
            SaveToPreferences();
            TeamFollowChanged?.Invoke(teamId, nowFollowed);
        }

        public HashSet<int> GetFollowedIds() => new(_followedIds);

        /// <summary>
        /// Primary team is intentionally independent of the followed set —
        /// a team can be primary without being followed. Set from Settings
        /// (Default team). MyTeams treats changes to this as a filter
        /// change: it re-points at the new team immediately.
        /// </summary>
        public int? GetPrimaryTeamId()
        {
            var id = Preferences.Default.Get(PrimaryTeamKey, -1);
            return id > 0 ? id : null;
        }

        public void SetPrimaryTeam(int? teamId)
        {
            if (teamId.HasValue)
                Preferences.Default.Set(PrimaryTeamKey, teamId.Value);
            else
                Preferences.Default.Remove(PrimaryTeamKey);

            PrimaryTeamChanged?.Invoke(teamId);
        }

        private HashSet<int> LoadFromPreferences()
        {
            var raw = Preferences.Default.Get(FollowedTeamsKey, string.Empty);
            if (string.IsNullOrEmpty(raw)) return [];
            return raw.Split(',')
                .Where(s => int.TryParse(s, out _))
                .Select(int.Parse)
                .ToHashSet();
        }

        private void SaveToPreferences()
        {
            Preferences.Default.Set(FollowedTeamsKey, string.Join(",", _followedIds));
        }
    }
}