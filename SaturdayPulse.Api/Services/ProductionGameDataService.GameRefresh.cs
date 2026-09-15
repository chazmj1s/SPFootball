using Microsoft.Extensions.Caching.Memory;
using SaturdayPulse.Contracts;
using SaturdayPulse.Contracts.Requests;
using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.Interfaces;
using SaturdayPulse.Models;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using SaturdayPulse.Utilities;
using SQLitePCL;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Timers;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// ProductionGameDataService — GameRefresh.
    /// Manual single-game live-score/odds refresh from CFBD.
    /// </summary>
    public partial class ProductionGameDataService
    {
        /// <summary>
        /// Refreshes score + odds for a single game from CFBD and returns the
        /// post-refresh state.
        ///
        /// The actual CFBD call and DB write live in GameDataService.RefreshGameAsync
        /// — confirmed live 2026-08-30 that CFBD's /lines?gameId=X returns
        /// homeScore/awayScore embedded alongside the odds array, so one call
        /// covers both. This method is just the cache + local-existence guard
        /// around that call.
        ///
        /// Returns null if gameId isn't found locally, or if it's still not
        /// found after the refresh (both → controller 404). Exceptions from the
        /// CFBD call (RefreshGameAsync calls response.EnsureSuccessStatusCode())
        /// propagate to the controller (→ 502/500).
        ///
        /// 60-second IMemoryCache entry per gameId — a re-tap inside that
        /// window returns the cached result instead of re-hitting CFBD.
        /// </summary>
        public async Task<GameRefreshResult?> GetGameAsync(int gameId, CancellationToken token = default)
        {
            var cacheKey = $"game-refresh-{gameId}";
            if (_memoryCache.TryGetValue(cacheKey, out GameRefreshResult? cached))
                return cached;

            var existingGame = await _uow.Games.GetByGameIdAsync(gameId, token);
            if (existingGame == null)
                return null;

            await _cfbdLoadService.RefreshGameAsync(gameId, token);

            // Re-read post-refresh state — RefreshGameAsync already called
            // SaveChangesAsync internally, so this reflects the new data.
            var refreshedGame = await _uow.Games.GetByGameIdAsync(gameId, token);
            if (refreshedGame == null)
                return null;

            var lines = await _uow.Lines.GetByGameIdAsync(gameId, token);

            var result = new GameRefreshResult
            {
                Games = refreshedGame,
                Lines = lines
            };

            _memoryCache.Set(cacheKey, result, TimeSpan.FromSeconds(60));
            return result;
        }
    }
}
