using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using SaturdayPulse.AdminBlazor.Services.Models;
using SaturdayPulse.Core.Content;
using SaturdayPulse.Core.Progress;

namespace SaturdayPulse.AdminBlazor.Services;

/// <summary>
/// Direct C# translation of admin-api.service.ts. Every method maps 1:1 to the
/// Angular original - same endpoint, same params, same optional-param behavior
/// (omit the query param entirely when not supplied, matching the TS "if (x) params.x = x" pattern).
///
/// Methods whose Angular return type is `Observable&lt;any&gt;` return JsonElement here.
/// As each admin page gets built out, its specific calls can be given a real DTO/return type.
/// </summary>
public class AdminApiService(HttpClient http)
{
    // ── Diagnostics ────────────────────────────────────────────────
    public async Task<DiagnosticDto?> GetDiagnosticAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<DiagnosticDto>("productiongamedata/diagnostic", JsonOpts, ct);

    // ── Weekly Ops ─────────────────────────────────────────────────
    public Task<JsonElement> LoadGamesAsync(int year, int week, CancellationToken ct = default) =>
        PostAsync("developer/loadGames", Query(("year", year), ("week", week)), ct);

    public Task<JsonElement> LoadLinesAsync(int year, int week, CancellationToken ct = default) =>
        PostAsync("developer/loadLines", Query(("year", year), ("week", week)), ct);

    public Task<JsonElement> UpdateTeamRecordsAsync(int? year = null, CancellationToken ct = default) =>
        PostAsync("developer/updateTeamRecords", Query(("year", year)), ct);

    public Task<JsonElement> ComputeWeeklyAsync(int? year = null, int? week = null, CancellationToken ct = default) =>
        PostAsync("developer/computeweekly", Query(("year", year), ("week", week)), ct);

    public Task<JsonElement> CalculateRollingAveragesAsync(int? year = null, int? week = null, CancellationToken ct = default) =>
        PostAsync("developer/calculateRollingAverages", Query(("year", year), ("week", week)), ct);

    public Task<JsonElement> AssignPostseasonWeeksAsync(int year, CancellationToken ct = default) =>
        PostAsync("developer/assignPostseasonWeeks", Query(("year", year)), ct);

    // ── Postseason Tagging ─────────────────────────────────────────
    public Task<JsonElement> LoadPostseasonGamesAsync(int year, CancellationToken ct = default) =>
        PostAsync("developer/loadGames", Query(("year", year), ("seasonType", "postseason")), ct);

    public async Task<List<PostseasonGameDto>> GetPostseasonGamesAsync(int year, CancellationToken ct = default)
    {
        var url = QueryHelpers.AddQueryString("productiongamedata/postseason/v2", Query(("year", year)));
        var result = await http.GetFromJsonAsync<PostseasonGamesResponse>(url, JsonOpts, ct);
        return result?.Games ?? new List<PostseasonGameDto>();
    }

    public Task<JsonElement> TagAsPlayoffAsync(IReadOnlyList<int> gameIds, CancellationToken ct = default) =>
        PostBodyAsync("developer/tagAsPlayoff", new { gameIds }, ct);

    public Task<JsonElement> UntagAsPlayoffAsync(IReadOnlyList<int> gameIds, CancellationToken ct = default) =>
        PostBodyAsync("developer/untagAsPlayoff", new { gameIds }, ct);

    // ── Season Setup ───────────────────────────────────────────────
    public Task<JsonElement> InitializeSeasonAsync(int year, CancellationToken ct = default) =>
        PostAsync("developer/initializeSeason", Query(("year", year)), ct);

    public Task<JsonElement> LoadConferencesAsync(CancellationToken ct = default) =>
        PostAsync("developer/loadConferences", null, ct);

    public Task<JsonElement> LoadTeamsAsync(int? year = null, CancellationToken ct = default) =>
        PostAsync("developer/loadTeams", Query(("year", year)), ct);

    public Task<JsonElement> BuildTeamsConferenceHistoryAsync(int startYear, CancellationToken ct = default) =>
        PostAsync("developer/buildTeamsConferenceHistory", Query(("startYear", startYear)), ct);

    public Task<JsonElement> LoadPortalAsync(int season, CancellationToken ct = default) =>
        PostAsync("developer/loadPortal", Query(("season", season)), ct);

    public Task<JsonElement> ComputePortalMetricsAsync(int season, CancellationToken ct = default) =>
        PostAsync("developer/computePortalMetrics", Query(("season", season)), ct);

    public Task<JsonElement> ComputeTierDiscountCoefficientsAsync(int season, int startYear = 1965, CancellationToken ct = default) =>
        PostAsync("developer/computeTierDiscountCoefficients", Query(("season", season), ("startYear", startYear)), ct);

    public Task<JsonElement> ComputeTierDiscountCoefficientsBulkAsync(int startSeason, int? throughSeason = null, int startYear = 1965, CancellationToken ct = default) =>
        PostAsync("developer/computeTierDiscountCoefficientsBulk", Query(("startSeason", startSeason), ("throughSeason", throughSeason), ("startYear", startYear)), ct);

    // ── Roster Capacity ────────────────────────────────────────────
    // New in this admin console — no Angular equivalent existed. Mirrors the
    // "Roster Capacity" endpoint region added to DeveloperController.cs.
    public Task<JsonElement> LoadRosterCapacityRosterAsync(int season, CancellationToken ct = default) =>
        PostAsync("developer/loadRosterCapacityRoster", Query(("season", season)), ct);

    public Task<JsonElement> LoadRosterCapacityRosterBothSeasonsAsync(int currentSeason, CancellationToken ct = default) =>
        PostAsync("developer/loadRosterCapacityRosterBothSeasons", Query(("currentSeason", currentSeason)), ct);

    public Task<JsonElement> LoadRosterCapacityStatsAsync(int season, CancellationToken ct = default) =>
        PostAsync("developer/loadRosterCapacityStats", Query(("season", season)), ct);

    public Task<JsonElement> LoadRosterCapacityCoachesAsync(int year, CancellationToken ct = default) =>
        PostAsync("developer/loadRosterCapacityCoaches", Query(("year", year)), ct);

    public Task<JsonElement> LoadAndApplyRosterCapacityRecruitingAsync(int year, CancellationToken ct = default) =>
        PostAsync("developer/loadAndApplyRosterCapacityRecruiting", Query(("year", year)), ct);

    public Task<JsonElement> LoadAndApplyPortalRatingsAsync(int season, CancellationToken ct = default) =>
        PostAsync("developer/loadAndApplyPortalRatings", Query(("season", season)), ct);

    // ── Users ──────────────────────────────────────────────────────
    // No Angular equivalent - new admin capability.
    public async Task<List<AdminUserSummaryDto>> GetUsersAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<AdminUserSummaryDto>>("developer/users", JsonOpts, ct) ?? new();

    public Task<JsonElement> GrantBetaAccessAsync(string userId, string productKey, CancellationToken ct = default) =>
        PostAsync("developer/grantBetaAccess", Query(("userId", userId), ("productKey", productKey)), ct);

    public Task<JsonElement> GrantSeasonPassAsync(string userId, string productKey, int season, CancellationToken ct = default) =>
        PostAsync("developer/grantSeasonPass", Query(("userId", userId), ("productKey", productKey), ("season", season)), ct);

    public Task<JsonElement> RevokeAccessAsync(string userId, string productKey, CancellationToken ct = default) =>
        PostAsync("developer/revokeAccess", Query(("userId", userId), ("productKey", productKey)), ct);

    // ── Content ────────────────────────────────────────────────────
    // No Angular equivalent - new admin capability. Talks to ContentController,
    // not DeveloperController - different route root, no "developer/" prefix.
    // ApplicationContentDocument comes from SaturdayPulse.Core - same type
    // Api serializes and Mobile will eventually deserialize, not a local copy.
    public async Task<ApplicationContentDocument?> GetContentAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<ApplicationContentDocument>("content", JsonOpts, ct);

    public Task<ApplicationContentDocument?> SaveContentAsync(ApplicationContentDocument document, CancellationToken ct = default) =>
        PutAsync<ApplicationContentDocument>("content", document, ct);

    // ── Metrics Rebuild ────────────────────────────────────────────
    public Task<JsonElement> BackfillRollingAveragesAsync(int? startYear = null, CancellationToken ct = default) =>
        PostAsync("developer/backfillRollingAverages", Query(("startYear", startYear)), ct);

    public Task<JsonElement> BackfillWeeklyRankingsAsync(int? startYear = null, CancellationToken ct = default) =>
        PostAsync("developer/backfillWeeklyRankings", Query(("startYear", startYear)), ct);

    // BackfillProjectionsAsync removed — endpoint no longer exists, see
    // DeveloperController/DeveloperService for why.

    // ── Metrics Rebuild — streaming (metrics-rebuild console) ────────────────
    // Same endpoints/params as above where a non-streaming route still exists
    // elsewhere (loadTeamsBulk etc. also power the Data Ops page); backfill*
    // routes were converted in place since this console is their only caller.
    public IAsyncEnumerable<ProgressUpdate> LoadTeamsBulkStreamAsync(int startYear, CancellationToken ct = default) =>
        PostStreamAsync("developer/loadTeamsBulk/stream", Query(("startYear", startYear)), ct);

    public IAsyncEnumerable<ProgressUpdate> LoadGamesBulkStreamAsync(int startYear, CancellationToken ct = default) =>
        PostStreamAsync("developer/loadGamesBulk/stream", Query(("startYear", startYear)), ct);

    public IAsyncEnumerable<ProgressUpdate> LoadLinesBulkStreamAsync(int startYear, CancellationToken ct = default) =>
        PostStreamAsync("developer/loadLinesBulk/stream", Query(("startYear", startYear)), ct);

    public IAsyncEnumerable<ProgressUpdate> BuildTeamsConferenceHistoryStreamAsync(
        int startYear, bool dryRun = false, CancellationToken ct = default) =>
        PostStreamAsync("developer/buildTeamsConferenceHistory/stream", Query(("startYear", startYear), ("dryRun", dryRun)), ct);

    public IAsyncEnumerable<ProgressUpdate> BackfillInitializeSeasonsStreamAsync(int? startYear = null, CancellationToken ct = default) =>
        PostStreamAsync("developer/backfillInitializeSeasons", Query(("startYear", startYear)), ct);

    public IAsyncEnumerable<ProgressUpdate> BackfillWeeklyRankingsStreamAsync(int? startYear = null, CancellationToken ct = default) =>
        PostStreamAsync("developer/backfillWeeklyRankings", Query(("startYear", startYear)), ct);

    // BackfillProjectionsStreamAsync removed — Option C in
    // WeeklyRankingsService.ComputeAndSaveAsync fully covers Projections
    // population now via BackfillWeeklyRankingsStreamAsync's per-week calls.

    // ── Portal coverage check ─────────────────────────────────────────────
    public async Task<PortalCoverageDto?> GetPortalCoverageAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<PortalCoverageDto>("developer/portalCoverage", JsonOpts, ct);

    public Task<JsonElement> BuildAvgScoreDifferentialsAsync(int? startYear = null, CancellationToken ct = default) =>
        PostAsync("developer/buildAvgScoreDifferentials", Query(("startYear", startYear)), ct);

    public Task<JsonElement> RecalculateScoreDeltasAsync(CancellationToken ct = default) =>
        PostAsync("developer/recalculateScoreDeltas", null, ct);

    public Task<JsonElement> CalculateMatchupHistoriesAsync(CancellationToken ct = default) =>
        PostAsync("developer/calculateMatchupHistories", null, ct);

    public Task<JsonElement> LoadTeamsBulkAsync(int startYear, CancellationToken ct = default) =>
        PostAsync("developer/loadTeamsBulk", Query(("startYear", startYear)), ct);

    public Task<JsonElement> LoadGamesBulkAsync(int startYear, CancellationToken ct = default) =>
        PostAsync("developer/loadGamesBulk", Query(("startYear", startYear)), ct);

    public Task<JsonElement> LoadLinesBulkAsync(int startYear, CancellationToken ct = default) =>
        PostAsync("developer/loadLinesBulk", Query(("startYear", startYear)), ct);

    // ── Analytics ──────────────────────────────────────────────────
    public async Task<ProjectionAccuracyResultDto?> GetProjectionAccuracyAsync(int? startYear = null, int? endYear = null, CancellationToken ct = default)
    {
        var url = QueryHelpers.AddQueryString("developer/projectionAccuracy", Query(("startYear", startYear), ("endYear", endYear)));
        return await http.GetFromJsonAsync<ProjectionAccuracyResultDto>(url, JsonOpts, ct);
    }

    public async Task<JsonElement> GetAnalyticsAsync(int? startYear = null, int? endYear = null, CancellationToken ct = default)
    {
        var url = QueryHelpers.AddQueryString("developer/analytics", Query(("startYear", startYear), ("endYear", endYear)));
        return await http.GetFromJsonAsync<JsonElement>(url, JsonOpts, ct);
    }

    public async Task<PortalAccuracyResultDto?> GetPortalAccuracyAsync(int? startYear = null, int? endYear = null, CancellationToken ct = default)
    {
        var url = QueryHelpers.AddQueryString("developer/portalAccuracy", Query(("startYear", startYear), ("endYear", endYear)));
        return await http.GetFromJsonAsync<PortalAccuracyResultDto>(url, JsonOpts, ct);
    }

    // ── Internals ────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Builds a query-string dictionary, silently omitting any (key, null) pair -
    /// mirrors the TS pattern of only setting params.x when x is truthy/provided.
    /// </summary>
    private static Dictionary<string, string?>? Query(params (string Key, object? Value)[] pairs)
    {
        var dict = pairs
            .Where(p => p.Value is not null)
            .ToDictionary(p => p.Key, p => p.Value?.ToString());
        return dict.Count == 0 ? null : dict;
    }

    private async Task<JsonElement> PostAsync(string path, Dictionary<string, string?>? query, CancellationToken ct)
    {
        var url = query is null ? path : QueryHelpers.AddQueryString(path, query);
        var response = await http.PostAsync(url, content: null, ct);
        return await ReadOrThrowAsync(response, ct);
    }

    private async Task<JsonElement> PostBodyAsync<TBody>(string path, TBody body, CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync(path, body, JsonOpts, ct);
        return await ReadOrThrowAsync(response, ct);
    }

    /// <summary>
    /// Streaming counterpart to PostAsync. Uses ResponseHeadersRead so the response
    /// body is read incrementally as the server yields items, instead of buffering
    /// the whole thing — that's the whole point, since these calls can run for
    /// tens of minutes and the caller wants to render each item as it arrives.
    ///
    /// Failure handling is asymmetric with PostAsync/ReadOrThrowAsync on purpose:
    /// a non-success status here can only mean the server rejected the request
    /// before writing any stream items (bad params, auth, etc.) — once the server
    /// starts streaming it has already committed to 200 OK, so per-item failures
    /// arrive as ProgressUpdate(Success: false) instead of an HTTP error. See
    /// DeveloperController's streaming actions for the server-side half of this.
    /// </summary>
    private async IAsyncEnumerable<ProgressUpdate> PostStreamAsync(
        string path, Dictionary<string, string?>? query, [EnumeratorCancellation] CancellationToken ct)
    {
        var url = query is null ? path : QueryHelpers.AddQueryString(path, query);
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new AdminApiException(body is { Length: > 0 } ? body : "check API logs");
        }

        await foreach (var item in response.Content.ReadFromJsonAsAsyncEnumerable<ProgressUpdate>(JsonOpts, ct))
        {
            if (item is not null)
                yield return item;
        }
    }

    private async Task<T?> PutAsync<T>(string path, object body, CancellationToken ct)
    {
        var response = await http.PutAsJsonAsync(path, body, JsonOpts, ct);
        var element = await ReadOrThrowAsync(response, ct);
        return element.ValueKind == JsonValueKind.Undefined ? default : element.Deserialize<T>(JsonOpts);
    }

    /// <summary>
    /// Reads the response as JsonElement on success. On failure, tries to pull a
    /// "message" field out of the error body before throwing, so callers can show
    /// something more useful than a bare status code - same intent as the Angular
    /// step-runner's `err?.error?.message ?? 'check API logs'`.
    /// </summary>
    private static async Task<JsonElement> ReadOrThrowAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var message = "check API logs";
            try
            {
                var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    message = m.GetString() ?? message;
            }
            catch (JsonException) { /* body wasn't JSON - fall back to default message */ }

            throw new AdminApiException(message);
        }

        return string.IsNullOrEmpty(body)
            ? default
            : JsonSerializer.Deserialize<JsonElement>(body, JsonOpts);
    }
}
