namespace SaturdayPulse.Extensions
{
    /// <summary>
    /// Single source of truth for "who is calling this endpoint."
    ///
    /// TEMPORARY (pre-Auth0): reads the X-User-Id header sent by the client.
    /// This is NOT a trustworthy identity claim — any caller can set this
    /// header to any value. Fine for a beta with no real accounts/payment
    /// yet; must be replaced before anything with real entitlement stakes
    /// depends on it.
    ///
    /// Once Auth0 JWT validation is wired in, this becomes the ONLY method
    /// that needs to change: swap the header read for
    /// context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value (the `sub`
    /// claim), and every controller that already calls GetUserId() picks up
    /// real auth with zero changes on their end.
    /// </summary>
    public static class HttpContextUserExtensions
    {
        public static string? GetUserId(this HttpContext context)
        {
            var userId = context.Request.Headers["X-User-Id"].ToString();
            return string.IsNullOrWhiteSpace(userId) ? null : userId;
        }
    }
}
