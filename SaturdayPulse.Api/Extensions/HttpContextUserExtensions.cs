using System.Security.Claims;

namespace SaturdayPulse.Extensions
{
    /// <summary>
    /// Single source of truth for "who is calling this endpoint."
    ///
    /// Dual-mode: prefers the Auth0 JWT `sub` claim (real, verified identity)
    /// and falls back to the X-User-Id header only when no JWT is present.
    /// The header path exists for local/dev tooling and any client not yet
    /// updated to send a bearer token; it is not a trustworthy identity claim
    /// on its own.
    /// </summary>
    public static class HttpContextUserExtensions
    {
        public static string? GetUserId(this HttpContext context)
        {
            var sub = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? context.User.FindFirst("sub")?.Value;
            if (!string.IsNullOrWhiteSpace(sub))
                return sub;

            var headerUserId = context.Request.Headers["X-User-Id"].ToString();
            return string.IsNullOrWhiteSpace(headerUserId) ? null : headerUserId;
        }
    }
}
