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

        /// <summary>Fires once the team list has (re)loaded.</summary>
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

        private async Task LoadAsync()
        {
            var result = await _api.GetTeamsAsync();
            _teams = result ?? new List<TeamInfo>();
            TeamsLoaded?.Invoke();
        }
    }
}
