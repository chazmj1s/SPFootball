using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    /// Registered as Singleton in Program.cs — cache persists across requests.
    /// Uses IServiceScopeFactory to resolve the Scoped IUnitOfWork safely
    /// from within a Singleton lifetime.
    ///
    /// Cache is keyed by year and invalidated when year changes.
    ///
    /// Two distinct caches, built together in the same pass:
    ///   GetAllProjections        — current, forward-looking best guess for
    ///                              games NOT YET PLAYED only. For standings /
    ///                              championship-qualifier simulation, which
    ///                              needs to project the remaining season, not
    ///                              re-litigate games that are already decided.
    ///   GetAllPregameProjections — the pregame projection for EVERY game this
    ///                              year, played or not — read straight from
    ///                              Projections, not resolved against Games.
    ///                              For "predicted vs actual" display (Schedule,
    ///                              My Teams, Postseason), where a played
    ///                              game's ORIGINAL pregame prediction is wanted
    ///                              alongside its real result, not superseded
    ///                              by it. Under the current calc design a game
    ///                              has at most one Projection row ever — it's
    ///                              written once, at the game's own native
    ///                              week, and never touched again once the game
    ///                              is marked played — so this is safe to read
    ///                              unconditionally with no "which snapshot"
    ///                              logic, same as GetAllProjections.
    ///
    /// REBUILT — GetAllProjections was selecting "freshest projection snapshot
    /// strictly before the game's own week" (p.Week &lt; gameWeek), a leftover
    /// from when a game could have multiple Projection rows, one per as-of
    /// week. Under the current calc design a game has at most one Projection
    /// row ever, so p.Week &lt; gameWeek was never true for any game, and this
    /// cache built to permanently empty with no error. Now reads straight from
    /// ResolvedGameResults (the view backing IUnitOfWork.ResolvedGameResults),
    /// which already resolves real-vs-projected with no per-consumer "which
    /// snapshot" logic needed. Also reads HomePoints/AwayPoints directly
    /// instead of reconstructing scores from PredictedTotal/PredictedSpread —
    /// same rounding-consistency reasoning as GamePredictionService.
    /// BuildProjection.
    /// </summary>
    public class ProjectionCacheService
    {
        private readonly IServiceScopeFactory             _scopeFactory;
        private readonly ILogger<ProjectionCacheService>  _logger;

        private readonly SemaphoreSlim                    _lock = new(1, 1);
        private          int?                             _cachedYear;
        private          Dictionary<int, GamePrediction>  _cache = new();
        private          Dictionary<int, GamePrediction>  _pregameCache = new();

        public ProjectionCacheService(
            IServiceScopeFactory scopeFactory,
            ILogger<ProjectionCacheService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

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
        /// Returns projections for games NOT YET PLAYED this season — see
        /// class remarks. For "predicted vs actual" display that needs a
        /// projection even for already-played games, use
        /// GetAllPregameProjections instead.
        /// </summary>
        public async Task<Dictionary<int, GamePrediction>> GetAllProjections(
            int year, CancellationToken token = default)
        {
            await EnsureCacheAsync(year, token);
            return _cache;
        }

        /// <summary>
        /// Returns the pregame projection for every game this season, played
        /// or not — see class remarks. Use for schedule/My Teams/postseason
        /// display ("predicted vs actual"). For forward-looking simulation
        /// (standings, championship qualifiers), use GetAllProjections instead
        /// — that one deliberately excludes already-decided games.
        /// </summary>
        public async Task<Dictionary<int, GamePrediction>> GetAllPregameProjections(
            int year, CancellationToken token = default)
        {
            await EnsureCacheAsync(year, token);
            return _pregameCache;
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
            var scope = _scopeFactory.CreateScope();
            try
            {
                if (_cachedYear == year && _cache.Count > 0) return;

                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                var resolved = await uow.ResolvedGameResults.GetByYearAsync(year, token);
                var allRawProjections = await uow.Projections.GetByYearAsync(year, token);

                // GameId is not guaranteed unique per year — e.g. two teams that
                // played each other in multiple different years (2021 Alabama /
                // 1999 LSU) can produce duplicate GameId rows across the season's
                // Projections/ResolvedGameResults query. Group-and-take-first
                // instead of a bare ToDictionary so a duplicate degrades to a
                // logged warning instead of an unhandled ArgumentException.
                var projectionByGameId = allRawProjections
                    .GroupBy(p => p.GameId)
                    .ToDictionary(g => g.Key, g => g.First());

                if (projectionByGameId.Count != allRawProjections.Count)
                {
                    _logger.LogWarning(
                        "ProjectionCacheService: {DuplicateCount} duplicate GameId row(s) in Projections for year {Year}; keeping first occurrence per GameId.",
                        allRawProjections.Count - projectionByGameId.Count, year);
                }

                // Only unplayed games need a "projection" entry — a played game's
                // real result is read straight from Games elsewhere, not from this
                // cache.
                var projectedResolved = resolved.Where(r => r.IsProjected).ToList();
                var newCache = projectedResolved
                    .GroupBy(r => r.GameId)
                    .ToDictionary(
                        g => g.Key,
                        g =>
                        {
                            var r = g.First();
                            return new GamePrediction
                            {
                                GameId = r.GameId,
                                Week = r.Week,
                                PredictedTeamScore = r.HomePoints,
                                PredictedOpponentScore = r.AwayPoints,
                                ExpectedMargin = projectionByGameId.TryGetValue(r.GameId, out var proj)
                                                        ? (double)proj.PredictedSpread : 0
                            };
                        });

                if (newCache.Count != projectedResolved.Count)
                {
                    _logger.LogWarning(
                        "ProjectionCacheService: duplicate GameId row(s) in ResolvedGameResults (projected) for year {Year}; keeping first occurrence per GameId.",
                        year);
                }

                // Pregame: every game's locked Projection row, unconditionally —
                // read straight from Projections, not resolved against Games, so
                // a played game's original pregame prediction survives here even
                // though it's excluded from newCache above.
                var newPregameCache = allRawProjections
                    .GroupBy(p => p.GameId)
                    .ToDictionary(
                        g => g.Key,
                        g =>
                        {
                            var p = g.First();
                            return new GamePrediction
                            {
                                GameId                 = p.GameId,
                                Week                   = p.Week,
                                PredictedTeamScore     = p.HomePoints,
                                PredictedOpponentScore = p.AwayPoints,
                                ExpectedMargin         = (double)p.PredictedSpread
                            };
                        });

                _cache = newCache;
                _pregameCache = newPregameCache;
                _cachedYear = year;
            }
            finally
            {
                await ((IAsyncDisposable)scope).DisposeAsync();
                _lock.Release();
            }
        }
    }
}
