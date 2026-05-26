using SaturdayPulse.Api.Contracts.Responses;
using SaturdayPulse.Models;

namespace SaturdayPulse.Interfaces
{
    public interface IGameDataService
    {
        Task UpdateTeamRecordsAsync(int? targetYear = null, CancellationToken token = default);

        // ── CFBD V2 — Single load ─────────────────────────────────────────────
        Task<int> BuildAvgScoreDifferentialsAsync(int startYear, CancellationToken token = default);
        Task<int> LoadConferencesAsync(CancellationToken token = default);
        Task<int> LoadTeamsAsync(int? year = null, CancellationToken token = default);
        Task<int> LoadGamesAsync(int year, int? week = null, CancellationToken token = default);
        Task<int> LoadLinesAsync(int year, int week, CancellationToken token = default);
        Task<int> WeeklyRefreshAsync(int year, int week, CancellationToken token = default);
        Task<int> AssignPostseasonWeeksAsync(int year, CancellationToken token = default);

        // ── CFBD V2 — Bulk load ───────────────────────────────────────────────
        Task<int> LoadTeamsBulkAsync(int startYear, CancellationToken token = default);
        Task<int> LoadGamesBulkAsync(int startYear, CancellationToken token = default);
        Task<int> LoadLinesBulkAsync(int startYear, CancellationToken token = default);
        Task<int> BuildTeamsConferenceHistoryAsync(int startYear, CancellationToken token = default);
        Task<int> AssignPostseasonWeeksBulkAsync(int startYear, CancellationToken token = default);
    }
}