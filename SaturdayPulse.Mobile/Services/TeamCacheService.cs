using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Singleton cache for the full FBS team list (teams/v2).
    ///
    /// MyTeams is now the app's default landing page, so it becomes the
    /// natural place that first populates this cache on startup. Settings
    /// (Default team picker) and anywhere else that needs the full team
    /// list can call EnsureLoadedAsync and read Teams without firing a
    /// second network call.
    ///
    /// Register as a singleton in MauiProgram alongside FollowService.
    /// </summary>
    public class TeamCacheService
    {
        private readonly GameDataApiService _api;
        private List<TeamInfo> _teams = new();
        private Task? _loadTask;

        public IReadOnlyList<TeamInfo> Teams => _teams;
        public bool IsLoaded => _teams.Count > 0;

        /// <summary>Fires once the team list has (re)loaded successfully.</summary>
        public event Action? TeamsLoaded;

        public TeamCacheService(GameDataApiService api)
        {
            _api = api;
        }

        /// <summary>
        /// Loads the team list if it hasn't been loaded yet. Safe to call
        /// from multiple ViewModels concurrently — concurrent callers await
        /// the same in-flight request instead of firing duplicate calls.
        /// </summary>
        public Task EnsureLoadedAsync()
        {
            if (IsLoaded) return Task.CompletedTask;
            return _loadTask ??= LoadAsync();
        }

        /// <summary>Forces a reload — call after Settings edits if the team list itself can change (rare).</summary>
        public Task RefreshAsync()
        {
            _loadTask = LoadAsync();
            return _loadTask;
        }

        public TeamInfo? GetTeam(int teamId) =>
            _teams.FirstOrDefault(t => t.TeamID == teamId);

        /// <summary>
        /// Bug fix (2026-09-14): a failed or empty GetTeamsAsync() call used
        /// to get memoized forever in _loadTask — IsLoaded stayed false, but
        /// EnsureLoadedAsync()'s `_loadTask ??= LoadAsync()` would never
        /// re-invoke LoadAsync() on later calls, since _loadTask was already
        /// non-null. GetTeam() then returned null permanently for every id,
        /// and BuildChips()/DefaultTeamDisplay silently dropped teams for
        /// the rest of the session. Clearing _loadTask on a failed/empty
        /// result lets the next EnsureLoadedAsync() actually retry.
        /// </summary>
        private async Task LoadAsync()
        {
            try
            {
                var result = await _api.GetTeamsAsync();
                _teams = result ?? new List<TeamInfo>();
            }
            catch
            {
                _teams = new List<TeamInfo>();
                // Swallowed deliberately: LoadAsync is fired from
                // OnPrimaryTeamChanged (async void) and similar paths where
                // an unhandled exception would crash rather than just
                // leaving the cache empty. Falls through to the retry reset
                // below like any other empty result.
            }

            if (IsLoaded)
                TeamsLoaded?.Invoke();
            else
                _loadTask = null; // allow the next EnsureLoadedAsync() to retry
        }
    }
}