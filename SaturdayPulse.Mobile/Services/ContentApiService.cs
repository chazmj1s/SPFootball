using System.Net.Http.Json;
using System.Text.Json;
using SaturdayPulse.Core.Content;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Fetches the single ApplicationContentDocument (About/Privacy/Terms/
    /// Season Pass/FAQ/Announcements/Release Notes + SupportEmail) from
    /// GET /api/content. Public endpoint, no auth needed - matches
    /// ContentController.cs on the API side, which is deliberately not
    /// behind [Authorize] so someone can read Terms of Service before
    /// they've logged in.
    ///
    /// ApplicationContentDocument/ContentSection come from SaturdayPulse.Core -
    /// same type Api serializes and AdminBlazor edits, not a local copy.
    /// </summary>
    public class ContentApiService
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public ContentApiService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        /// <summary>Returns null on any failure - callers should treat that as
        /// "nothing to show yet" (e.g. skip the About panel's content) rather
        /// than surfacing an error, since this isn't user-critical data.</summary>
        public async Task<ApplicationContentDocument?> GetContentAsync()
        {
            try
            {
                using var response = await _httpClient.GetAsync("content");
                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[ContentAPI] GetContent failed: {response.StatusCode}");
                    return null;
                }

                return await response.Content.ReadFromJsonAsync<ApplicationContentDocument>(_jsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ContentAPI] Error GetContent: {ex.Message}");
                return null;
            }
        }
    }
}
