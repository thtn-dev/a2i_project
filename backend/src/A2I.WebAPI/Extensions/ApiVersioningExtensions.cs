using A2I.Application.Common;
using Microsoft.OpenApi.Models;

namespace A2I.WebAPI.Extensions;

/// <summary>
///     Extension methods for configuring API versioning
/// </summary>
public static class ApiVersioningExtensions
{
    /// <summary>
    ///     Configures API versioning for the application
    /// </summary>
    public static IServiceCollection AddApiVersioningConfiguration(this IServiceCollection services)
    {
        // For now, we'll use URL-based versioning (e.g., /api/v1/...)
        // This is simple and explicit

        services.AddEndpointsApiExplorer();
        services.AddOpenApi();

        return services;
    }

    /// <summary>
    ///     Maps all versioned endpoints
    /// </summary>
    public static WebApplication MapVersionedEndpoints(this WebApplication app)
    {
        // Map v1 endpoints
        app.MapV1Endpoints();

        // Future: Map v2 endpoints
        // app.MapV2Endpoints();

        return app;
    }

    /// <summary>
    ///     Maps all v1 endpoints
    /// </summary>
    private static void MapV1Endpoints(this WebApplication app)
    {
        // This will be called from Program.cs
        // Individual endpoint groups will be registered in their respective files
    }

    /// <summary>
    ///     Configure OpenAPI/Swagger for versioned APIs
    /// </summary>
    public static IServiceCollection ConfigureOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "A2I Stripe Subscription API",
                    Version = "v1",
                    Description = "API for managing Stripe subscriptions, customers, and invoices",
                    Contact = new OpenApiContact
                    {
                        Name = "A2I Support",
                        Email = "support@a2i.com"
                    }
                };
                return Task.CompletedTask;
            });
            
            // Transform schema để support generic types cho Orval
            options.AddSchemaTransformer((schema, context, cancellationToken) =>
            {
                var type = context.JsonTypeInfo.Type;

                // Handle ApiResponse<T> generic type
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ApiResponse<>))
                {
                    var dataType = type.GetGenericArguments()[0];
                    
                    // Custom schema ID for Orval to recognize as generic
                    // Format: ApiResponse_T where T is the actual type name
                    var schemaId = $"ApiResponseOf{dataType.Name}";
                    
                    // Ensure data property has correct reference
                    if (schema.Properties.TryGetValue("data", out var dataProperty))
                    {
                        // Add description for better documentation
                        dataProperty.Description = $"The response data of type {dataType.Name}";
                    }

                    // Mark as nullable where appropriate
                    if (schema.Properties.TryGetValue("data", out var arg1Property))
                    {
                        arg1Property.Nullable = true;
                    }
                    if (schema.Properties.TryGetValue("message", out var property))
                    {
                        property.Nullable = true;
                    }
                }

                // Handle base ApiResponse (non-generic)
                if (type == typeof(ApiResponse))
                {
                    if (schema.Properties.TryGetValue("message", out var property))
                    {
                        property.Nullable = true;
                    }
                }

                return Task.CompletedTask;
            });

            // Add operation transformers for better documentation
            options.AddOperationTransformer((operation, context, cancellationToken) =>
            {
                // Add common response headers
                foreach (var response in operation.Responses.Values)
                {
                    response.Headers ??= new Dictionary<string, OpenApiHeader>();
                    
                    if (!response.Headers.ContainsKey("X-Request-ID"))
                    {
                        response.Headers["X-Request-ID"] = new OpenApiHeader
                        {
                            Description = "Request identifier for tracking",
                            Schema = new OpenApiSchema { Type = "string" }
                        };
                    }
                }

                return Task.CompletedTask;
            });
        });
        
        return services;
    }
}