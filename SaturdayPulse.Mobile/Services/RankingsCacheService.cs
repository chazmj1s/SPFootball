using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Singleton cache holding the current year+week power rankings.
    /// Mirrors GameDataCacheService's shape exactly, so PowerRankingsViewModel
    /// and MyTeamsViewModel share one fetch instead of each hitting
    /// GetPowerRankingsAsync independently.
    ///
    /// Rankings are week-scoped (unlike games, which are year-scoped only),
    /// so the cache key is (year, week) rather than just year.
    ///
    /// Follow-flag stamping (IsFollowed) moves here from
    /// PowerRankingsViewModel.LoadDataAsync — it's now done once, on the
    /// shared TeamRanking instances, so both consumers see the same stamped
    /// objects rather than each stamping their own copy.
    /// </summary>
    public class RankingsCacheService
    {
        private readonly GameDataApiService _apiService;
        private readonly FollowService      _followService;

        private List<TeamRanking> _allRankings = new();
        private int _loadedYear = 0;
        private int _loadedWeek = -1;

        public RankingsCacheService(GameDataApiService apiService, FollowService followService)
        {
            _apiService    = apiService;
            _followService = followService;

            _followService.TeamFollowChanged += OnTeamFollowChanged;
        }

        // ── Public state ──────────────────────────────────────────────────

        /// <summary>All teams for the currently loaded (year, week). Empty until first load.</summary>
        public IReadOnlyList<TeamRanking> AllRankings => _allRankings;

        public int  LoadedYear => _loadedYear;
        public int  LoadedWeek => _loadedWeek;
        public bool HasData    => _allRankings.Count > 0;

        // ── Events ────────────────────────────────────────────────────────

        /// <summary>Fired when the cache is reloaded or a follow flag is re-stamped.</summary>
        public event Action? CacheUpdated;

        // ── Load ──────────────────────────────────────────────────────────

        /// <summary>
        /// Returns rankings for the requested (year, week). Uses cache if both
        /// match and forceReload is false. Otherwise fetches from the API and
        /// re-caches. Cancellation is the caller's responsibility (same as
        /// PowerRankingsViewModel's existing CancellationTokenSource pattern) —
        /// this method itself doesn't take a token, so callers that need
        /// rapid-switch cancellation should still guard around their own call.
        /// </summary>
        public async Task<IReadOnlyList<TeamRanking>> GetRankingsAsync(
            int year, int week, bool forceReload = false)
        {
            if (!forceReload && year == _loadedYear && week == _loadedWeek && _allRankings.Count > 0)
                return _allRankings;

            var teams = await _apiService.GetPowerRankingsAsync(year, week);
            if (teams == null || teams.Count == 0)
            {
                _allRankings = new List<TeamRanking>();
                _loadedYear  = year;
                _loadedWeek  = week;
                CacheUpdated?.Invoke();
                return _allRankings;
            }

            StampFollowAndTierFlags(teams);

            _allRankings = teams;
            _loadedYear  = year;
            _loadedWeek  = week;
            CacheUpdated?.Invoke();
            return _allRankings;
        }

        // ── Flag stamping ────────────────────────────────────────────────

        private void StampFollowAndTierFlags(IList<TeamRanking> teams)
        {
            var followedIds = _followService.GetFollowedIds();
            foreach (var t in teams)
            {
                t.IsFollowed = followedIds.Contains(t.TeamID);
                t.IsTop25    = t.OverallRank > 0 && t.OverallRank <= 25;
            }
        }

        private void OnTeamFollowChanged(int teamId, bool isFollowed)
        {
            var team = _allRankings.FirstOrDefault(t => t.TeamID == teamId);
            if (team != null)
            {
                team.IsFollowed = isFollowed;
                MainThread.BeginInvokeOnMainThread(() => CacheUpdated?.Invoke());
            }
        }
    }
}
