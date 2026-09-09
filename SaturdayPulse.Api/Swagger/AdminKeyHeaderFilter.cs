using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using SaturdayPulse.Controllers;

namespace SaturdayPulse.Swagger
{
    /// <summary>
    /// Adds an X-Admin-Key header input box in Swagger UI for every action on
    /// DeveloperController — mirrors the shared-secret check in
    /// AdminKeyAttribute (see SaturdayPulse.Filters.AdminKeyAttribute).
    ///
    /// Scoped to DeveloperController specifically. Add other [AdminKey]-gated
    /// controllers here if/when they exist.
    /// </summary>
    public class AdminKeyHeaderFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (context.MethodInfo.DeclaringType != typeof(DeveloperController))
                return;

            operation.Parameters ??= new List<IOpenApiParameter>();

            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "X-Admin-Key",
                In = ParameterLocation.Header,
                Required = true,
                Description = "Shared-secret admin key checked by AdminKeyAttribute against Admin:ApiKey config.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
        }
    }
}