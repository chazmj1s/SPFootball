using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using SaturdayPulse.Controllers;

namespace SaturdayPulse.Swagger
{
    /// <summary>
    /// Adds an X-User-Id header input box in Swagger UI for every action on
    /// UserController — mirrors the temporary header-based identity resolution
    /// in HttpContextUserExtensions.GetUserId(). Lets you spoof different
    /// users while testing without reaching for curl/Postman.
    ///
    /// Scoped to UserController specifically rather than every endpoint —
    /// if other controllers start calling HttpContext.GetUserId() too (e.g.
    /// entitlement-gated endpoints on ProductionGameDataController), add
    /// their declaring type to the check below.
    ///
    /// DELETE THIS FILE along with the rest of the X-User-Id plumbing once
    /// Auth0 JWT validation replaces it — Swagger's "Authorize" button with
    /// a bearer token takes over this role at that point.
    /// </summary>
    public class XUserIdHeaderFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (context.MethodInfo.DeclaringType != typeof(UserController))
                return;

            operation.Parameters ??= new List<IOpenApiParameter>();

            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "X-User-Id",
                In = ParameterLocation.Header,
                Required = true,
                Description = "Temporary pre-Auth0 identity header. Use the seeded GUID or any test UserId.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
        }
    }
}
