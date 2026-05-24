using SaturdayPulse.Contracts;
using SaturdayPulse.Contracts.Responses;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Single source of truth for game score projections.
    /// All endpoints (GetSchedule, GetProjectedStandings,
    /// GetProjectedChampionshipQualifiers) must use this service
    /// rather than calling GamePredictionService directly.
    ///
    /// Cache is keyed by year and invalidated when year changes.
    /// Registered as Singleton in Program.cs.
    ///
    /// For each game, picks the freshest projection snapshot strictly before
    /// the game's own week. Week 0 snapshots (created by InitializeSeasonAsync)
    /// provide projections for week 1 games since 0 &lt; 1.
    /// </summary>
    public class ProjectionCacheService
    {
        private readonly IUnitOfWork _uow;

        private readonly SemaphoreSlim                    _lock = new(1, 1);
        private          int?                             _cachedYear;
        private          Dictionary<int, GamePrediction>  _cache = new();

        public ProjectionCacheService(IUnitOfWork uow) => _uow = uow;

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the projection for a single game.
        /// Builds the full-season cache if it does not exist yet.
        /// </summary>
        public async Task<GamePrediction?> GetProjection(
            int year, int gameId, CancellationToken token = default)
        {
            await EnsureCacheAsync(year, token);
            _cache.TryGetValue(gameId, out var pred);
            return pred;
        }

        /// <summary>
        /// Returns projections for all games in the season.
        /// </summary>
        public async Task<Dictionary<int, GamePrediction>> GetAllProjections(
            int year, CancellationToken token = default)
        {
            await EnsureCacheAsync(year, token);
            return _cache;
        }

        /// <summary>
        /// Force-invalidates the cache (e.g. after weekly rankings are updated).
        /// </summary>
        public void Invalidate() => _cachedYear = null;

        // ── Private ───────────────────────────────────────────────────────────────

        private async Task EnsureCacheAsync(int year, CancellationToken token)
        {
            if (_cachedYear == year && _cache.Count > 0) return;

            await _lock.WaitAsync(token);
            try
            {
                if (_cachedYear == year && _cache.Count > 0) return;

                var games = await _uow.Games.GetByYearAsync(year, token);
                var gameWeekById = games
                    .Where(g => g.GameId > 0)
                    .ToDictionary(g => g.GameId, g => g.Week);

                var allProjections = await _uow.Projections.GetByYearAsync(year, token);

                // For each game pick the freshest snapshot strictly before the game's
                // own week. Week 0 snapshots cover week 1 games (0 < 1).
                var newCache = allProjections
                    .Where(p => gameWeekById.TryGetValue(p.GameId, out var gameWeek)
                                && p.Week < gameWeek)
                    .GroupBy(p => p.GameId)
                    .Select(g => g.OrderByDescending(p => p.Week).First())
                    .ToDictionary(
                        p => p.GameId,
                        p => new GamePrediction
                        {
                            PredictedTeamScore     = (double)(p.PredictedTotal + p.PredictedSpread) / 2.0,
                            PredictedOpponentScore = (double)(p.PredictedTotal - p.PredictedSpread) / 2.0,
                            ExpectedMargin         = (double)p.PredictedSpread
                        });

                _cache      = newCache;
                _cachedYear = year;
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
