using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Singleton cache holding the current year's full game list.
    /// Shared by ScheduleViewModel and ProjectionsViewModel so postseason
    /// games (bowls + playoffs) come from the same source as regular games.
    /// Listens for follow/favorite changes and re-stamps cached games,
    /// firing CacheUpdated so consumers can refilter their views.
    /// </summary>
    public class GameDataCacheService
    {
        private readonly GameDataApiService  _apiService;
        private readonly FollowService       _followService;
        private readonly PersonalGameService _personalGameService;

        private List<GameResult> _allGames   = new();
        private int              _loadedYear = 0;

        public GameDataCacheService(
            GameDataApiService apiService,
            FollowService followService,
            PersonalGameService personalGameService)
        {
            _apiService          = apiService;
            _followService       = followService;
            _personalGameService = personalGameService;

            _followService.TeamFollowChanged         += OnTeamFollowChanged;
            _personalGameService.GameFavoritedChange += OnGameFavoritedChange;
        }

        // ── Public state ──────────────────────────────────────────────────

        /// <summary>All games for the currently loaded year. Empty until first load.</summary>
        public IReadOnlyList<GameResult> AllGames => _allGames;

        /// <summary>The year currently in the cache (0 if nothing loaded yet).</summary>
        public int LoadedYear => _loadedYear;

        public bool HasData => _allGames.Count > 0;

        // ── Events ────────────────────────────────────────────────────────

        /// <summary>Fired when the cache is reloaded or game flags are re-stamped.</summary>
        public event Action? CacheUpdated;

        // ── Load ──────────────────────────────────────────────────────────

        /// <summary>
        /// Returns games for the requested year. Uses cache if year matches and
        /// forceReload is false. Otherwise fetches from the API and caches.
        /// </summary>
        public async Task<IReadOnlyList<GameResult>> GetGamesForYearAsync(
            int year, bool forceReload = false)
        {
            if (!forceReload && year == _loadedYear && _allGames.Count > 0)
                return _allGames;

            var games = await _apiService.GetScheduleAsync(year);
            if (games == null || games.Count == 0)
            {
                _allGames   = new List<GameResult>();
                _loadedYear = year;
                CacheUpdated?.Invoke();
                return _allGames;
            }

            // Sequence + flag stamping (moved out of ScheduleViewModel)
            for (int i = 0; i < games.Count; i++)
                games[i].SequenceNumber = i + 1;

            StampFollowAndFavoriteFlags(games);

            _allGames   = games;
            _loadedYear = year;
            CacheUpdated?.Invoke();
            return _allGames;
        }

        // ── Flag stamping ────────────────────────────────────────────────

        private void StampFollowAndFavoriteFlags(IList<GameResult> games)
        {
            var followedIds = _followService.GetFollowedIds();
            foreach (var g in games)
            {
                g.HomeIsFollowed    = followedIds.Contains(g.HomeId);
                g.VisitorIsFollowed = followedIds.Contains(g.AwayId);
                g.IsGameFavorited   = _personalGameService.IsFavorited(g.AwayId, g.HomeId);
            }
        }

        private void OnTeamFollowChanged(int teamId, bool isFollowed)
        {
            foreach (var g in _allGames)
            {
                if (g.HomeId == teamId) g.HomeIsFollowed    = isFollowed;
                if (g.AwayId == teamId) g.VisitorIsFollowed = isFollowed;
            }
            MainThread.BeginInvokeOnMainThread(() => CacheUpdated?.Invoke());
        }

        private void OnGameFavoritedChange(string key, bool isFavorited)
        {
            foreach (var g in _allGames)
            {
                if (PersonalGameService.Key(g.AwayId, g.HomeId) == key)
                    g.IsGameFavorited = isFavorited;
            }
            MainThread.BeginInvokeOnMainThread(() => CacheUpdated?.Invoke());
        }
    }
}
