using Microsoft.AspNetCore.Mvc;
using SaturdayPulse.Core.Content;
using SaturdayPulse.Filters;
using SaturdayPulse.Services;

namespace SaturdayPulse.Controllers
{
    /// <summary>
    /// Serves and edits the single ApplicationContent document (About, Privacy
    /// Policy, Terms of Service, Season Pass, FAQ, Announcements, Release Notes).
    ///
    /// GET is intentionally NOT behind any auth - the mobile app needs to be
    /// able to show Terms of Service / Privacy Policy to someone who isn't
    /// logged in yet (e.g. before they create an account).
    ///
    /// PUT is gated by [AdminKey] (X-Admin-Key shared secret, same interim
    /// boundary as DeveloperController). The API is publicly reachable, so an
    /// ungated PUT would let anyone overwrite the Privacy Policy, Terms, and
    /// support email. Swap for AdminOnlyAttribute when the console gets a
    /// real login.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ContentController(
        ContentService contentService,
        ILogger<ContentController> logger) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken token = default)
        {
            try
            {
                return Ok(await contentService.GetContentAsync(token));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving application content");
                return StatusCode(500, "An error occurred while retrieving content.");
            }
        }

        [HttpPut]
        [AdminKey]
        public async Task<IActionResult> Update(
            [FromBody] ApplicationContentDocument document, CancellationToken token = default)
        {
            try
            {
                return Ok(await contentService.SaveContentAsync(document, token));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error saving application content");
                return StatusCode(500, "An error occurred while saving content.");
            }
        }
    }
}
