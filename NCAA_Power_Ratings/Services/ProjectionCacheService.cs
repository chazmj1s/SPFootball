using NCAA_Power_Ratings.Data;
using Microsoft.EntityFrameworkCore;
using NCAA_Power_Ratings.Contracts.Requests;
using NCAA_Power_Ratings.Contracts.Responses;


namespace NCAA_Power_Ratings.Services
{
    /// <summary>
    /// Single source of truth for game score projections.
    /// All endpoints (GetSchedule, GetProjectedStandings,
    /// GetProjectedChampionshipQualifiers) must use this service
    /// rather than calling GamePredictionService directly.
    ///
    /// Cache is keyed by (year, gameId) and invalidated when year changes.
    /// Registered as Singleton in Program.cs.
    /// </summary>
    public class ProjectionCacheService
    {
        private readonly IDbContextFactory<NCAAContext> _contextFactory;
        private readonly GamePredictionService          _predictionService;

        private readonly SemaphoreSlim _lock = new(1, 1);
        private int?                              _cachedYear;
        private Dictionary<int, GamePrediction>   _cache = new();

        public ProjectionCacheService(
            IDbContextFactory<NCAAContext> contextFactory,
            GamePredictionService predictionService)
        {
            _contextFactory    = contextFactory;
            _predictionService = predictionService;
        }

        /// <summary>
        /// Returns the projection for a single game.
        /// If the full-season cache for this year doesn't exist yet, builds it.
        /// </summary>
        public async Task<GamePrediction?> GetProjection(
            int year,
            int gameId,
            CancellationToken token = default)
        {
            await EnsureCacheAsync(year, token);
            _cache.TryGetValue(gameId, out var pred);
            return pred;
        }

        /// <summary>
        /// Returns projections for all conference games in the season.
        /// </summary>
        public async Task<Dictionary<int, GamePrediction>> GetAllProjections(
            int year,
            CancellationToken token = default)
        {
            await EnsureCacheAsync(year, token);
            return _cache;
        }

        /// <summary>
        /// Force-invalidates the cache (e.g. when team records are updated).
        /// </summary>
        public void Invalidate() => _cachedYear = null;

        // ── Private ──────────────────────────────────────────────────────

        private async Task EnsureCacheAsync(int year, CancellationToken token)
        {
            if (_cachedYear == year && _cache.Count > 0) return;

            await _lock.WaitAsync(token);
            try
            {
                // Double-check inside lock
                if (_cachedYear == year && _cache.Count > 0) return;

                await using var context = await _contextFactory.CreateDbContextAsync(token);

                var teams = await context.Team.ToDictionaryAsync(t => t.TeamID, token);

                // Load all regular season games
                var allGames = await context.Game
                    .Where(g => g.Year == year && g.Week < 16)
                    .ToListAsync(token);

                // Build matchup requests for ALL games (played and unplayed)
                // This ensures projections exist for every game regardless of score
                var matchupRequests = allGames
                    .Where(g => teams.ContainsKey(g.WinnerId) && teams.ContainsKey(g.LoserId))
                    .Select(g => new MatchupRequest
                    {
                        TeamName     = teams[g.WinnerId].TeamName,
                        OpponentName = teams[g.LoserId].TeamName,
                        Location     = g.Location,
                        Week         = g.Week
                    })
                    .ToList();

                var predictions = await _predictionService.PredictMatchups(
                    year, matchupRequests, token);

                // Key predictions by GameId using team name matching
                var newCache = new Dictionary<int, GamePrediction>();

                foreach (var g in allGames)
                {
                    if (!teams.TryGetValue(g.WinnerId, out var wTeam)) continue;
                    if (!teams.TryGetValue(g.LoserId,  out var lTeam)) continue;

                    var pred = predictions.FirstOrDefault(p =>
                        p.TeamName     == wTeam.TeamName &&
                        p.OpponentName == lTeam.TeamName);

                    if (pred != null)
                        newCache[g.Id] = pred;
                }

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
