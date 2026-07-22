using System.Net.Http.Json;
using SaturdayPulse.Mobile.Services;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Posts beta feedback to the Discord webhook configured in
    /// ApiConfiguration.DiscordWebhook. Fire-and-report: returns bool
    /// success/failure, no retry logic yet — matches the tolerance level
    /// of a beta feedback channel, not a critical path.
    /// </summary>
    public class FeedbackService(HttpClient httpClient)
    {
        private readonly HttpClient _httpClient = httpClient;

        public async Task<bool> SubmitFeedbackAsync(string message, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            // Discord webhook message cap.
            if (message.Length > 2000)
                message = message[..2000];

            try
            {
                var payload = new { content = message };
                using var response = await _httpClient.PostAsJsonAsync(
                    ApiConfiguration.DiscordWebhook, payload, token);

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Feedback] Submit failed: {ex.Message}");
                return false;
            }
        }
    }
}
