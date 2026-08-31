using SaturdayPulse.Api.Contracts.Responses;
using SaturdayPulse.Core.Progress;
using SaturdayPulse.Models;

namespace SaturdayPulse.Interfaces
{
    public interface IGameDataService
    {
        Task<int> SetSeasonTypeAsync(List<int> gameIds, string seasonType, CancellationToken token = default);
        Task UpdateTeamRecordsAsync(int? targetYear = null, CancellationToken token = default);

        // ── CFBD V2 — Single load ─────────────────────────────────────────────
        Task<int> BuildAvgScoreDifferentialsAsync(int startYear, CancellationToken token = default);
        Task<int> LoadConferencesAsync(CancellationToken token = default);
        Task<int> LoadTeamsAsync(int? year = null, CancellationToken token = default);
        Task<int> LoadGamesAsync(int year, int? week = null, CancellationToken token = default);
        Task<int> LoadLinesAsync(int year, int week, CancellationToken token = default);
        Task<int> WeeklyRefreshAsync(int year, int week, CancellationToken token = default);
        Task<int> RefreshGameAsync(int gameId, CancellationToken token = default);
        Task<int> AssignPostseasonWeeksAsync(int year, CancellationToken token = default);
        Task<int> LoadRosterCapacityRosterAsync(int season, CancellationToken token = default);
        Task<int> LoadRosterCapacityStatsAsync(int season, CancellationToken token = default);
        Task<int> LoadRosterCapacityCoachesAsync(int year, CancellationToken token = default);

        // ── CFBD V2 — Bulk load ───────────────────────────────────────────────
        Task<int> LoadTeamsBulkAsync(int startYear, CancellationToken token = default);
        Task<int> LoadGamesBulkAsync(int startYear, CancellationToken token = default);
        Task<int> LoadLinesBulkAsync(int startYear, CancellationToken token = default);
        Task<int> BuildTeamsConferenceHistoryAsync(int startYear, CancellationToken token = default);
        Task<int> AssignPostseasonWeeksBulkAsync(int startYear, CancellationToken token = default);
        Task<int> LoadPortalAsync(int season, CancellationToken token = default);
        Task<int> LoadPortalBulkAsync(int startSeason, CancellationToken token = default);
        Task<int> LoadRosterCapacityRecruitingAsync(int year, CancellationToken token);
        Task<(int RecruitsLoaded, int RatingsApplied)> LoadAndApplyRosterCapacityRecruitingAsync(int year, CancellationToken token);
        Task<(int PortalLoaded, int RatingsApplied)> LoadAndApplyPortalRatingsAsync(int season, CancellationToken token = default);

        // ── CFBD V2 — Bulk load (streaming) ───────────────────────────────────
        // Yield one ProgressUpdate per unit processed instead of returning a single
        // total at the end, so the admin console can show live per-year progress
        // instead of going silent for the full duration of the call.
        IAsyncEnumerable<ProgressUpdate> LoadTeamsBulkStreamAsync(int startYear, CancellationToken token = default);
        IAsyncEnumerable<ProgressUpdate> LoadGamesBulkStreamAsync(int startYear, CancellationToken token = default);
        IAsyncEnumerable<ProgressUpdate> LoadLinesBulkStreamAsync(int startYear, CancellationToken token = default);

        // ── Portal coverage check ─────────────────────────────────────────────
        // Read-only diagnostic — no write, safe to call any time. Reports which
        // seasons since portal data became available (2021) have zero PortalEntries.
        Task<PortalCoverageResult> GetPortalCoverageAsync(CancellationToken token = default);
    }
}