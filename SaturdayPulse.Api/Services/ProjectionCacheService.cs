using SaturdayPulse.Contracts;
using SaturdayPulse.Contracts.Requests;
using SaturdayPulse.Contracts.Responses;

namespace SaturdayPulse.Services
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
        private readonly IUnitOfWork _uow;
        private readonly GamePredictionService _predictionService;

        private readonly SemaphoreSlim _lock = new(1, 1);
        private int? _cachedYear;
        private Dictionary<int, GamePrediction> _cache = new();

        public ProjectionCacheService(
            IUnitOfWork uow,
            GamePredictionService predictionService)
        {
            _uow = uow;
            _predictionService = predictionService;
        }

        /// <summary>
        /// Returns the projection for a single game.
        /// Builds the full-season cache if it doesn't exist yet.
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
        /// Force-invalidates the cache (e.g. when team records are updated).
        /// </summary>
        public void Invalidate() => _cachedYear = null;

        // ── Private ───────────────────────────────────────────────────────────────

        private async Task EnsureCacheAsync(int year, CancellationToken token)
        {
            if (_cachedYear == year && _cache.Count > 0) return;

            await _lock.WaitAsync(token);
            try
            {
                // Double-check inside lock
                if (_cachedYear == year && _cache.Count > 0) return;

                var teams = await _uow.Teams.GetTeamDictionaryAsync(token);
                var allGames = await _uow.Games.GetByYearAsync(year, token);

                // Filter to regular season only
                var regularSeasonGames = allGames.Where(g => g.Week < 16).ToList();

                // Build matchup requests for ALL games (played and unplayed)
                var matchupRequests = regularSeasonGames
                    .Where(g => teams.ContainsKey(g.WinnerId) && teams.ContainsKey(g.LoserId))
                    .Select(g => new MatchupRequest
                    {
                        TeamName = teams[g.WinnerId].TeamName,
                        OpponentName = teams[g.LoserId].TeamName,
                        Location = g.Location,
                        Week = g.Week
                    })
                    .ToList();

                var predictions = await _predictionService.PredictMatchups(
                    year, matchupRequests, token);

                // Key predictions by GameId using team name matching
                var newCache = new Dictionary<int, GamePrediction>();

                foreach (var g in regularSeasonGames)
                {
                    if (!teams.TryGetValue(g.WinnerId, out var wTeam)) continue;
                    if (!teams.TryGetValue(g.LoserId, out var lTeam)) continue;

                    var pred = predictions.FirstOrDefault(p =>
                        p.TeamName == wTeam.TeamName &&
                        p.OpponentName == lTeam.TeamName);

                    if (pred != null)
                        newCache[g.Id] = pred;
                }

                _cache = newCache;
                _cachedYear = year;
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
