using Microsoft.AspNetCore.Mvc;
using SaturdayPulse.Contracts.Requests;
using SaturdayPulse.Extensions;
using SaturdayPulse.Services;

namespace SaturdayPulse.Controllers
{
    /// <summary>
    /// Profile, contact info, and follow management.
    ///
    /// Identity resolution goes through HttpContext.GetUserId() (see
    /// HttpContextUserExtensions) — any other controller that needs to know
    /// "who is calling" should call the same extension, not duplicate this.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class UserController(
        UserProfileService userProfileService,
        ILogger<UserController> logger) : ControllerBase
    {
        private IActionResult? TryResolveUserId(out string userId)
        {
            var resolved = HttpContext.GetUserId();
            if (resolved == null)
            {
                userId = string.Empty;
                return BadRequest("Unable to resolve user identity.");
            }
            userId = resolved;
            return null;
        }

        #region Profile

        /// <summary>
        /// GET /api/user/me — returns the current profile, provisioning one
        /// on first-ever contact from this UserId.
        /// </summary>
        [HttpGet("me")]
        public async Task<IActionResult> GetMe(CancellationToken token = default)
        {
            if (TryResolveUserId(out var userId) is { } badRequest) return badRequest;

            try
            {
                var profile = await userProfileService.GetOrCreateProfileAsync(userId, defaultEmail: null, token);
                return Ok(profile);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving profile for {UserId}", userId);
                return StatusCode(500, "An error occurred while retrieving the profile.");
            }
        }

        /// <summary>PATCH /api/user/me/handle</summary>
        [HttpPatch("me/handle")]
        public async Task<IActionResult> UpdateHandle(
            [FromBody] UpdateHandleRequest request, CancellationToken token = default)
        {
            if (TryResolveUserId(out var userId) is { } badRequest) return badRequest;

            try
            {
                await userProfileService.UpdateHandleAsync(userId, request.Handle, token);
                return NoContent();
            }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch (InvalidOperationException ex) { return Conflict(ex.Message); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating handle for {UserId}", userId);
                return StatusCode(500, "An error occurred while updating the handle.");
            }
        }

        /// <summary>PATCH /api/user/me/primary-team</summary>
        [HttpPatch("me/primary-team")]
        public async Task<IActionResult> UpdatePrimaryTeam(
            [FromBody] UpdatePrimaryTeamRequest request, CancellationToken token = default)
        {
            if (TryResolveUserId(out var userId) is { } badRequest) return badRequest;

            try
            {
                await userProfileService.UpdatePrimaryTeamAsync(userId, request.TeamId, token);
                return NoContent();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating primary team for {UserId}", userId);
                return StatusCode(500, "An error occurred while updating the primary team.");
            }
        }

        /// <summary>PATCH /api/user/me/email</summary>
        [HttpPatch("me/email")]
        public async Task<IActionResult> UpdateEmail(
            [FromBody] UpdateEmailRequest request, CancellationToken token = default)
        {
            if (TryResolveUserId(out var userId) is { } badRequest) return badRequest;

            try
            {
                await userProfileService.UpdateEmailAsync(userId, request.Email, token);
                return NoContent();
            }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch (InvalidOperationException ex) { return Conflict(ex.Message); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating email for {UserId}", userId);
                return StatusCode(500, "An error occurred while updating the email.");
            }
        }

        /// <summary>PATCH /api/user/me/phone</summary>
        [HttpPatch("me/phone")]
        public async Task<IActionResult> UpdatePhone(
            [FromBody] UpdatePhoneRequest request, CancellationToken token = default)
        {
            if (TryResolveUserId(out var userId) is { } badRequest) return badRequest;

            try
            {
                await userProfileService.UpdatePhoneAsync(
                    userId, request.PhoneNumber, request.MarketingSmsConsent, token);
                return NoContent();
            }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating phone for {UserId}", userId);
                return StatusCode(500, "An error occurred while updating the phone number.");
            }
        }

        #endregion

        #region Follows

        [HttpGet("me/followed-teams")]
        public async Task<IActionResult> GetFollowedTeams(CancellationToken token = default)
        {
            if (TryResolveUserId(out var userId) is { } badRequest) return badRequest;

            try
            {
                var teams = await userProfileService.GetFollowedTeamsAsync(userId, token);
                return Ok(teams.Select(f => f.TeamId));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving followed teams for {UserId}", userId);
                return StatusCode(500, "An error occurred while retrieving followed teams.");
            }
        }

        [HttpPut("me/followed-teams/{teamId:int}")]
        public async Task<IActionResult> FollowTeam(int teamId, CancellationToken token = default)
        {
            if (TryResolveUserId(out var userId) is { } badRequest) return badRequest;

            try
            {
                await userProfileService.FollowTeamAsync(userId, teamId, token);
                return NoContent();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error following team {TeamId} for {UserId}", teamId, userId);
                return StatusCode(500, "An error occurred while following the team.");
            }
        }

        [HttpDelete("me/followed-teams/{teamId:int}")]
        public async Task<IActionResult> UnfollowTeam(int teamId, CancellationToken token = default)
        {
            if (TryResolveUserId(out var userId) is { } badRequest) return badRequest;

            try
            {
                await userProfileService.UnfollowTeamAsync(userId, teamId, token);
                return NoContent();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error unfollowing team {TeamId} for {UserId}", teamId, userId);
                return StatusCode(500, "An error occurred while unfollowing the team.");
            }
        }

        [HttpGet("me/followed-games")]
        public async Task<IActionResult> GetFollowedGames(CancellationToken token = default)
        {
            if (TryResolveUserId(out var userId) is { } badRequest) return badRequest;

            try
            {
                var games = await userProfileService.GetFollowedGamesAsync(userId, token);
                return Ok(games.Select(f => new { f.Team1Id, f.Team2Id }));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving followed games for {UserId}", userId);
                return StatusCode(500, "An error occurred while retrieving followed games.");
            }
        }

        /// <summary>PUT /api/user/me/followed-games?team1Id=201&team2Id=251</summary>
        [HttpPut("me/followed-games")]
        public async Task<IActionResult> FollowGame(
            [FromQuery] int team1Id, [FromQuery] int team2Id, CancellationToken token = default)
        {
            if (TryResolveUserId(out var userId) is { } badRequest) return badRequest;

            try
            {
                await userProfileService.FollowGameAsync(userId, team1Id, team2Id, token);
                return NoContent();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error following game {T1}v{T2} for {UserId}", team1Id, team2Id, userId);
                return StatusCode(500, "An error occurred while following the game.");
            }
        }

        [HttpDelete("me/followed-games")]
        public async Task<IActionResult> UnfollowGame(
            [FromQuery] int team1Id, [FromQuery] int team2Id, CancellationToken token = default)
        {
            if (TryResolveUserId(out var userId) is { } badRequest) return badRequest;

            try
            {
                await userProfileService.UnfollowGameAsync(userId, team1Id, team2Id, token);
                return NoContent();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error unfollowing game {T1}v{T2} for {UserId}", team1Id, team2Id, userId);
                return StatusCode(500, "An error occurred while unfollowing the game.");
            }
        }

        #endregion
    }
}
