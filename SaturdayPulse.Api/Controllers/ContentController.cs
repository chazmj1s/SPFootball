using Microsoft.AspNetCore.Mvc;
using SaturdayPulse.Core.Content;
using SaturdayPulse.Services;

namespace SaturdayPulse.Controllers
{
    /// <summary>
    /// Serves and edits the single ApplicationContent document (About, Privacy
    /// Policy, Terms of Service, Season Pass, FAQ, Announcements, Release Notes).
    ///
    /// GET is intentionally NOT behind [Authorize] - the mobile app needs to be
    /// able to show Terms of Service / Privacy Policy to someone who isn't
    /// logged in yet (e.g. before they create an account). PUT has no separate
    /// admin gate either for now, same trust boundary as DeveloperController -
    /// there's no role/policy infrastructure beyond UserProfile.IsAdmin today,
    /// and this endpoint isn't reachable by anything but the admin console and
    /// whoever knows the URL. Revisit if/when this API is exposed beyond a
    /// single admin's laptop.
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
