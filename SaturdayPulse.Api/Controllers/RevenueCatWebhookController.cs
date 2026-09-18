using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SaturdayPulse.Configuration;
using SaturdayPulse.Contracts.Requests;
using SaturdayPulse.Services;

namespace SaturdayPulse.Controllers
{
    /// <summary>
    /// POST /api/revenuecat/webhook — receives RevenueCat server events.
    /// Anonymous at the ASP.NET layer (RevenueCat has no Auth0 token); the
    /// shared Authorization header value is the gate instead. Fails closed if
    /// no token is configured.
    ///
    /// Returns 200 for anything it deliberately ignores (TEST, unknown
    /// product, anonymous user) so RevenueCat doesn't retry unfixable events;
    /// returns 500 only for unexpected failures so RevenueCat does retry.
    /// </summary>
    [ApiController]
    [Route("api/revenuecat")]
    [AllowAnonymous]
    public class RevenueCatWebhookController(
        IOptions<RevenueCatSettings> settings,
        RevenueCatWebhookService webhookService,
        ILogger<RevenueCatWebhookController> logger) : ControllerBase
    {
        [HttpPost("webhook")]
        public async Task<IActionResult> Receive(
            [FromBody] RevenueCatWebhookRequest request, CancellationToken token = default)
        {
            if (!IsAuthorized()) return Unauthorized();

            if (request.Event == null) return BadRequest("Missing event.");

            try
            {
                await webhookService.HandleAsync(request.Event, token);
                return Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Error handling RevenueCat event {Type} ({EventId})",
                    request.Event.Type, request.Event.Id);
                return StatusCode(500, "An error occurred while processing the event.");
            }
        }

        private bool IsAuthorized()
        {
            var expected = settings.Value.WebhookAuthToken?.Trim() ?? string.Empty;
            var provided = Request.Headers.Authorization.ToString().Trim();

            // Accept the secret bare, or with a "Bearer " prefix.
            const string bearerPrefix = "Bearer ";
            var hasBearerPrefix = provided.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase);
            var candidate = hasBearerPrefix ? provided[bearerPrefix.Length..].Trim() : provided;

            var authorized = expected.Length > 0 &&
                CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(candidate),
                    Encoding.UTF8.GetBytes(expected));

            if (!authorized)
            {
                // Lengths and flags only - never log the secret or the header value.
                logger.LogWarning(
                    "RevenueCat webhook auth failed: headerPresent={HeaderPresent}, headerLength={HeaderLength}, bearerPrefix={BearerPrefix}, expectedConfigured={ExpectedConfigured}, expectedLength={ExpectedLength}",
                    provided.Length > 0, candidate.Length, hasBearerPrefix, expected.Length > 0, expected.Length);
            }

            return authorized;
        }
    }
}
