using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SaturdayPulse.Filters
{
    /// <summary>
    /// Local-only admin console gate — checks a shared secret header
    /// (X-Admin-Key) against a server-configured value, rather than
    /// requiring a real login. Chosen 2026-09-04 as the interim boundary for
    /// DeveloperController: AdminBlazor runs local-only (Charlie's machine,
    /// maybe a future local Ubuntu box) with no login of its own, so there's
    /// no Auth0 identity to check IsAdmin against. Swap for AdminOnlyAttribute
    /// (real Auth0 login + IsAdmin check — already built, see
    /// AdminOnlyAttribute.cs, just not wired up here) once a second admin
    /// exists and a login screen for the console gets built.
    ///
    /// Fails closed: a missing/unconfigured "Admin:ApiKey" server-side
    /// rejects every request (500, not a silent pass-through) rather than
    /// accepting anything.
    ///
    /// Fixed-time comparison — a plain string == on a secret header is a
    /// (minor, but free-to-avoid) timing side channel.
    /// </summary>
    public class AdminKeyAttribute : Attribute, IAuthorizationFilter
    {
        private const string HeaderName = "X-Admin-Key";

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var expectedKey = config["Admin:ApiKey"];

            if (string.IsNullOrEmpty(expectedKey))
            {
                context.Result = new StatusCodeResult(StatusCodes.Status500InternalServerError);
                return;
            }

            var providedKey = context.HttpContext.Request.Headers[HeaderName].ToString();

            if (string.IsNullOrEmpty(providedKey) || !SecureEquals(providedKey, expectedKey))
            {
                context.Result = new UnauthorizedResult();
                return;
            }
        }

        private static bool SecureEquals(string provided, string expected)
        {
            var providedBytes = Encoding.UTF8.GetBytes(provided);
            var expectedBytes = Encoding.UTF8.GetBytes(expected);

            return providedBytes.Length == expectedBytes.Length
                && CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
        }
    }
}