using Microsoft.OpenApi;
using Shell.Core.Tenancy;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Shell.Host;

/// <summary>Adds the <c>X-Tenant-Host</c> header field to every operation in Swagger UI (local testing of tenants).</summary>
internal sealed class TenantHostHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = TenantResolutionMiddleware.TenantHostHeader,
            In = ParameterLocation.Header,
            Required = false,
            Description = "Tenant domain, e.g. tenant-a.localhost or tenant-b.localhost (requires Tenancy:AllowHostHeaderOverride).",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
        });
    }
}
