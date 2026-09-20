using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SaturdayPulse.Configuration;
using SaturdayPulse.Extensions;
using SaturdayPulse.Services;

namespace SaturdayPulse.Controllers
{
    /// <summary>
    /// POST /api/feedback - forwards beta feedback from the mobile app to the
    /// Discord channel. The webhook URL lives only in server configuration
    /// (Feedback__DiscordWebhookUrl), never in the app binary.
    ///
    /// Requires a valid Auth0 token AND an active Season Pass (the same gate
    /// the app applies to show the feedback panel), and is rate limited per
    /// user so a single account can't flood the channel. Mentions are
    /// suppressed so user text can't ping @everyone or roles.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class FeedbackController(
        UserProfileService userProfileService,
        IHttpClientFactory httpClientFactory,
        IOptions<FeedbackSettings> settings,
        ILogger<FeedbackController> logger) : ControllerBase
    {
        // Discord caps message content at 2000 characters; leave room for the
        // handle prefix added below.
        private const int MaxMessageLength = 1800;
        private const int MaxMessagesPerWindow = 5;
        private static readonly TimeSpan RateWindow = TimeSpan.FromMinutes(10);
        private static readonly ConcurrentDictionary<string, Queue<DateTime>> RecentByUser = new();

        public sealed class FeedbackRequest
        {
            public string? Message { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> Submit(
            [FromBody] FeedbackRequest request, CancellationToken token = default)
        {
            string? userId = HttpContext.GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest("Unable to resolve user identity.");
            }

            string message = request.Message?.Trim() ?? string.Empty;
            if (message.Length == 0)
            {
                return BadRequest("Message is required.");
            }

            if (message.Length > MaxMessageLength)
            {
                message = message[..MaxMessageLength];
            }

            string webhookUrl = settings.Value.DiscordWebhookUrl;
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                logger.LogError("Feedback__DiscordWebhookUrl is not configured; feedback cannot be delivered.");
                return StatusCode(503, "Feedback is not available right now.");
            }

            try
            {
                var profile = await userProfileService.GetProfileAsync(userId, token);
                if (profile == null || !profile.IsEntitled)
                {
                    return Forbid();
                }

                if (!TryTakeSlot(userId))
                {
                    return StatusCode(StatusCodes.Status429TooManyRequests,
                        "Too many messages. Please wait a few minutes and try again.");
                }

                var payload = new
                {
                    content = $"**{profile.Handle}**: {message}",
                    allowed_mentions = new { parse = Array.Empty<string>() }
                };

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));

                HttpClient client = httpClientFactory.CreateClient();
                using HttpResponseMessage response =
                    await client.PostAsJsonAsync(webhookUrl, payload, timeout.Token);

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Discord rejected feedback from {UserId}: HTTP {Status}",
                        userId, (int)response.StatusCode);
                    return StatusCode(502, "Feedback could not be delivered.");
                }

                return NoContent();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Deliberately not logging the webhook URL.
                logger.LogError(ex, "Error forwarding feedback from {UserId}", userId);
                return StatusCode(502, "Feedback could not be delivered.");
            }
        }

        private static bool TryTakeSlot(string userId)
        {
            DateTime now = DateTime.UtcNow;
            Queue<DateTime> recent = RecentByUser.GetOrAdd(userId, _ => new Queue<DateTime>());

            lock (recent)
            {
                while (recent.Count > 0 && now - recent.Peek() > RateWindow)
                {
                    recent.Dequeue();
                }

                if (recent.Count >= MaxMessagesPerWindow)
                {
                    return false;
                }

                recent.Enqueue(now);
                return true;
            }
        }
    }
}
