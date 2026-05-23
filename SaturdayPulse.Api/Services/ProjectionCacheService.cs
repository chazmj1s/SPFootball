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
                if (_cachedYear == year && _cache.Count > 0) return;

                // Get game weeks so we know the snapshot cutoff per game
                var games = await _uow.Games.GetByYearAsync(year, token);
                var gameWeekById = games
                    .Where(g => g.GameId > 0)
                    .ToDictionary(g => g.GameId, g => g.Week);

                // Load all projections for the year
                var allProjections = await _uow.Projections.GetByYearAsync(year, token);

                // For each game, pick the projection with the highest snapshot
                // week that is strictly less than the game's own week
                var newCache = allProjections
                    .Where(p => gameWeekById.TryGetValue(p.GameId, out var gameWeek)
                                && p.Week < gameWeek)
                    .GroupBy(p => p.GameId)
                    .Select(g => g.OrderByDescending(p => p.Week).First())
                    .ToDictionary(
                        p => p.GameId,
                        p => new GamePrediction
                        {
                            PredictedTeamScore = (double)(p.PredictedTotal + p.PredictedSpread) / 2.0,
                            PredictedOpponentScore = (double)(p.PredictedTotal - p.PredictedSpread) / 2.0,
                            ExpectedMargin = (double)p.PredictedSpread
                        });

                _cache = newCache;
                _cachedYear = year;
                Console.WriteLine($"Cache:{year}: {newCache.Count()}");
            }
            finally
            {
                _lock.Release();
            }
        }
        //private async Task EnsureCacheAsync(int year, CancellationToken token)
        //{
        //    if (_cachedYear == year && _cache.Count > 0) return;

        //    await _lock.WaitAsync(token);
        //    try
        //    {
        //        // Double-check inside lock
        //        if (_cachedYear == year && _cache.Count > 0) return;

        //        var teams = await _uow.Team.GetTeamDictionaryAsync(token);
        //        var allGames = await _uow.Game.GetByYearAsync(year, token);
        //        var maxWeek = allGames.Max(g => g.Week);
        //        // Filter to regular season only
        //        var regularSeasonGames = allGames.Where(g => g.Week < maxWeek).ToList();

        //        // Build matchup requests for ALL games (played and unplayed)
        //        var matchupRequests = regularSeasonGames
        //            .Where(g => teams.ContainsKey(g.WinnerId) && teams.ContainsKey(g.LoserId))
        //            .Select(g => new MatchupRequest
        //            {
        //                TeamName = teams[g.WinnerId].TeamName,
        //                OpponentName = teams[g.LoserId].TeamName,
        //                Location = g.Location,
        //                Week = g.Week
        //            })
        //            .ToList();

        //        var predictions = await _predictionService.PredictMatchups(
        //            year, matchupRequests, token);

        //        // Key predictions by GameId using team name matching
        //        var newCache = new Dictionary<int, GamePrediction>();

        //        foreach (var g in regularSeasonGames)
        //        {
        //            if (!teams.TryGetValue(g.WinnerId, out var wTeam)) continue;
        //            if (!teams.TryGetValue(g.LoserId, out var lTeam)) continue;

        //            var pred = predictions.FirstOrDefault(p =>
        //                p.TeamName == wTeam.TeamName &&
        //                p.OpponentName == lTeam.TeamName);

        //            if (pred != null)
        //                newCache[g.Id] = pred;
        //        }

        //        _cache = newCache;
        //        _cachedYear = year;
        //    }
        //    finally
        //    {
        //        _lock.Release();
        //    }
        //}
    }
}
