namespace A2I.WebAPI.Extensions;

/// <summary>
/// Extension methods for configuring API versioning
/// </summary>
public static class ApiVersioningExtensions
{
    /// <summary>
    /// Configures API versioning for the application
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
    /// Maps all versioned endpoints
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
    /// Maps all v1 endpoints
    /// </summary>
    private static void MapV1Endpoints(this WebApplication app)
    {
        // This will be called from Program.cs
        // Individual endpoint groups will be registered in their respective files
    }

    /// <summary>
    /// Configure OpenAPI/Swagger for versioned APIs
    /// </summary>
    public static IServiceCollection ConfigureOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                document.Info = new()
                {
                    Title = "A2I Stripe Subscription API",
                    Version = "v1",
                    Description = "API for managing Stripe subscriptions, customers, and invoices",
                    Contact = new()
                    {
                        Name = "A2I Support",
                        Email = "support@a2i.com"
                    }
                };
                return Task.CompletedTask;
            });
        });

        return services;
    }
}