using SaturdayPulse.Api.Contracts.Responses;
using SaturdayPulse.Models;

namespace SaturdayPulse.Interfaces
{
    public interface IGameDataService
    {
        // ── Legacy ────────────────────────────────────────────────────────────
        Task<List<Game>> ExtractGameDataHistoryAsync(int? years);
        Task<int> UpdateGameDataForYearAndWeekAsync(int year, int week, CancellationToken token = default);
        Task<int> LoadGameHistoryFromFiles();
        Task<int> LoadTeamDataFromFile();
        Task UpdateTeamRecordsAsync(int? targetYear = null, CancellationToken token = default);
        Task<int> ProcessSingleFileAsync(string filePath, CancellationToken token = default);
        Task<int> UpdateGameDataFromFileAsync(string filePath, int year, int week, CancellationToken token = default);
        Task<List<CfbdTeamDto>> PreviewCfbdTeamsAsync(int? year = null, CancellationToken token = default);
        Task<List<CfbdGameDto>> PreviewCfbdGamesAsync(int year, int? week = null, CancellationToken token = default);

        // ── CFBD V2 — Single load ─────────────────────────────────────────────
        Task<int> LoadConferencesAsync(CancellationToken token = default);
        Task<int> LoadTeamsAsync(int? year = null, CancellationToken token = default);
        Task<int> LoadGamesAsync(int year, int? week = null, CancellationToken token = default);
        Task<int> LoadLinesAsync(int year, int week, CancellationToken token = default);
        Task<int> WeeklyRefreshAsync(int year, int week, CancellationToken token = default);

        // ── CFBD V2 — Bulk load ───────────────────────────────────────────────
        Task<int> LoadTeamsBulkAsync(int startYear, CancellationToken token = default);
        Task<int> LoadGamesBulkAsync(int startYear, CancellationToken token = default);
        Task<int> LoadLinesBulkAsync(int startYear, CancellationToken token = default);
        Task<int> BuildTeamsConferenceHistoryAsync(int startYear, CancellationToken token = default);
    }
}