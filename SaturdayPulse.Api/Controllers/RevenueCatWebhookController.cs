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
            var expected = settings.Value.WebhookAuthToken;
            if (string.IsNullOrEmpty(expected)) return false;

            var provided = Request.Headers.Authorization.ToString();
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(provided),
                Encoding.UTF8.GetBytes(expected));
        }
    }
}
