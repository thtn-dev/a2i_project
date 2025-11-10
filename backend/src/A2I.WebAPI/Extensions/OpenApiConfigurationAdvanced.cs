using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;
using System.Reflection;
using A2I.Application.Common;

namespace A2I.WebAPI.Extensions;

/// <summary>
/// Advanced OpenAPI configuration with full generic type support for Orval code generation
/// </summary>
public static class OpenApiConfigurationAdvanced
{
    public static IServiceCollection ConfigureOpenApi2(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            // Document-level configuration
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "A2I Stripe Subscription API",
                    Description = "API for managing Stripe subscriptions, customers, and invoices",
                    Version = "v1",
                    Contact = new OpenApiContact
                    {
                        Name = "A2I Support",
                        Email = "support@a2i.com"
                    }
                };

                // Add security schemes if needed
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, OpenApiSecurityScheme>();

                return Task.CompletedTask;
            });

            // Schema-level transformations for generic types
            options.AddSchemaTransformer(TransformGenericSchemas);

            // Operation-level transformations
            options.AddOperationTransformer(TransformOperations);
        });

        return services;
    }

    private static Task TransformGenericSchemas(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;

        // Handle ApiResponse<T>
        if (IsGenericApiResponse(type))
        {
            TransformApiResponseSchema(schema, type);
        }

        // Handle other generic types if needed
        // Example: PagedResult<T>, Result<T>, etc.
        
        return Task.CompletedTask;
    }

    private static void TransformApiResponseSchema(OpenApiSchema schema, Type type)
    {
        var dataType = type.GetGenericArguments()[0];
        
        // Improve schema documentation
        schema.Description ??= $"Standard API response wrapper containing {dataType.Name}";
        
        // Ensure proper nullability
        var nullableProperties = new[] { "data", "message" };
        foreach (var prop in nullableProperties)
        {
            if (schema.Properties.TryGetValue(prop, out var property))
            {
                property.Nullable = true;
            }
        }

        // Add property descriptions
        if (schema.Properties.TryGetValue("success", out var successProp))
        {
            successProp.Description = "Indicates whether the operation was successful";
        }
        
        if (schema.Properties.TryGetValue("data", out var dataProp))
        {
            dataProp.Description = $"The response data of type {dataType.Name}";
        }
        
        if (schema.Properties.TryGetValue("message", out var messageProp))
        {
            messageProp.Description = "Optional message providing additional context";
        }

        // Mark required properties
        schema.Required = new HashSet<string> { "success" };
    }

    private static Task TransformOperations(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        // Add standard response headers
        AddResponseHeaders(operation);

        // Improve response descriptions
        ImproveResponseDescriptions(operation);

        return Task.CompletedTask;
    }

    private static void AddResponseHeaders(OpenApiOperation operation)
    {
        var standardHeaders = new Dictionary<string, OpenApiHeader>
        {
            ["X-Request-ID"] = new()
            {
                Description = "Unique request identifier for tracking and debugging",
                Schema = new OpenApiSchema { Type = "string", Format = "uuid" }
            },
            ["X-RateLimit-Limit"] = new()
            {
                Description = "The maximum number of requests allowed in the current time window",
                Schema = new OpenApiSchema { Type = "integer" }
            },
            ["X-RateLimit-Remaining"] = new()
            {
                Description = "The number of requests remaining in the current time window",
                Schema = new OpenApiSchema { Type = "integer" }
            }
        };

        foreach (var response in operation.Responses.Values)
        {
            response.Headers ??= new Dictionary<string, OpenApiHeader>();
            
            foreach (var (headerName, header) in standardHeaders)
            {
                if (!response.Headers.ContainsKey(headerName))
                {
                    response.Headers[headerName] = header;
                }
            }
        }
    }

    private static void ImproveResponseDescriptions(OpenApiOperation operation)
    {
        // Improve standard response descriptions
        var responseDescriptions = new Dictionary<string, string>
        {
            ["200"] = "Request completed successfully",
            ["201"] = "Resource created successfully",
            ["204"] = "Request completed successfully with no content",
            ["400"] = "Bad request - Invalid input parameters or validation errors",
            ["401"] = "Unauthorized - Authentication required or failed",
            ["403"] = "Forbidden - Insufficient permissions",
            ["404"] = "Not found - The requested resource does not exist",
            ["409"] = "Conflict - Resource state conflict",
            ["422"] = "Unprocessable entity - Validation failed",
            ["500"] = "Internal server error - An unexpected error occurred",
            ["503"] = "Service unavailable - The service is temporarily unavailable"
        };

        foreach (var (statusCode, description) in responseDescriptions)
        {
            if (operation.Responses.TryGetValue(statusCode, out var response))
            {
                // Only update if current description is generic
                if (string.IsNullOrEmpty(response.Description) || 
                    response.Description.Equals(statusCode, StringComparison.OrdinalIgnoreCase))
                {
                    response.Description = description;
                }
            }
        }
    }

    private static bool IsGenericApiResponse(Type type)
    {
        return type.IsGenericType && 
               type.GetGenericTypeDefinition() == typeof(ApiResponse<>);
    }

    // Extension method to check for other generic patterns
    private static bool IsGenericType(Type type, Type genericTypeDefinition)
    {
        return type.IsGenericType && 
               type.GetGenericTypeDefinition() == genericTypeDefinition;
    }
}