using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Sends beta feedback to POST api/feedback on the SaturdayPulse API,
    /// authenticated with the current Auth0 access token. The API checks the
    /// Season Pass entitlement, applies rate limiting, and forwards the message
    /// to Discord - the webhook URL is server-side only and is never part of
    /// the app. Returns bool success/failure with no retry logic, matching the
    /// tolerance of a beta feedback channel.
    /// </summary>
    public class FeedbackService(HttpClient httpClient, AuthService authService)
    {
        // Keep in step with FeedbackController.MaxMessageLength on the API.
        private const int MaxMessageLength = 1800;

        private readonly HttpClient _httpClient = httpClient;
        private readonly AuthService _authService = authService;

        public async Task<bool> SubmitFeedbackAsync(string message, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            message = message.Trim();
            if (message.Length > MaxMessageLength)
                message = message[..MaxMessageLength];

            try
            {
                string? accessToken = await _authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(accessToken))
                {
                    System.Diagnostics.Debug.WriteLine("[Feedback] Not logged in; cannot send feedback.");
                    return false;
                }

                using var request = new HttpRequestMessage(HttpMethod.Post, "feedback")
                {
                    Content = JsonContent.Create(new { message })
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                using HttpResponseMessage response = await _httpClient.SendAsync(request, token);

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[Feedback] Submit failed: {(int)response.StatusCode}");
                }

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
