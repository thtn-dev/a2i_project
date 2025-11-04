using System.Threading.RateLimiting;
using A2I.Infrastructure.Caching;
using A2I.Infrastructure.Database;
using A2I.Infrastructure.StripeServices;
using A2I.WebAPI.Endpoints.Auth;
using A2I.WebAPI.Endpoints.Customers;
using A2I.WebAPI.Endpoints.Invoices;
using A2I.WebAPI.Endpoints.Subscriptions;
using A2I.WebAPI.Endpoints.System;
using A2I.WebAPI.Endpoints.Test;
using A2I.WebAPI.Extensions;
using A2I.WebAPI.Middlewares;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.RateLimiting;
using Scalar.AspNetCore;

namespace A2I.WebAPI;

public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // ==================== SERVICES CONFIGURATION ====================

        // Core services
        builder.Services.AddAuthorization();
        builder.Services.AddControllers();
        builder.Services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Instance = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
                context.ProblemDetails.Extensions["timestamp"] = DateTime.UtcNow;
            };
        });


        // Database & Infrastructure
        builder.Services.AddDatabaseServices(builder.Configuration, builder.Environment);
        builder.Services.AddIdentityServices(builder.Configuration);
        builder.Services.AddStripeServices(builder.Configuration);
        builder.Services.AddBackgroundJobServices();

        builder.Services.AddRateLimiter(rateLimiterOptions =>
        {
            rateLimiterOptions.AddFixedWindowLimiter("fixed", options =>
            {
                options.PermitLimit = 10;
                options.Window = TimeSpan.FromSeconds(10);
                options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                options.QueueLimit = 5;
            });

            rateLimiterOptions.AddSlidingWindowLimiter("sliding", options =>
            {
                options.PermitLimit = 20;
                options.Window = TimeSpan.FromSeconds(30);
                options.SegmentsPerWindow = 6;
                options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                options.QueueLimit = 10;
            });
        });

        // Hangfire for background jobs
        builder.Services.AddHangfire(config =>
        {
            config.UsePostgreSqlStorage(options =>
            {
                var databaseSection = builder.Configuration.GetSection(DatabaseOptions.SectionName);
                var dbOptions = databaseSection.Get<DatabaseOptions>();
                var connectionString = ServiceCollectionExtensions.BuildConnectionString(dbOptions!);

                options.UseNpgsqlConnection(connectionString);
            }, new PostgreSqlStorageOptions
            {
                PrepareSchemaIfNecessary = true,
                InvisibilityTimeout = TimeSpan.FromMinutes(30),
                QueuePollInterval = TimeSpan.FromSeconds(15),
                UseNativeDatabaseTransactions = true,
                EnableTransactionScopeEnlistment = true
            });
            config.UseSimpleAssemblyNameTypeSerializer();
            config.UseRecommendedSerializerSettings();
        });

        builder.Services.AddHangfireServer(options =>
        {
            options.WorkerCount = 5;
            options.Queues = ["stripe-webhooks", "emails", "default"];
        });
        
        builder.Services.AddCacheService(options =>
        {
            options.CacheType = CacheType.Redis;
            options.ConnectionString = "localhost:6379";
            options.DefaultExpirationMinutes = 30;
            options.InstanceName = "MyApp";
        });

        builder.Services.AddScoped<IStripeWebhookJob, StripeWebhookJob>();

        // API Configuration
        builder.Services.ConfigureOpenApi();

        // ==================== APP CONFIGURATION ====================

        var app = builder.Build();

        // ===== MIDDLEWARE PIPELINE (ORDER MATTERS!) =====

        // 1. Global exception handling (catches all unhandled exceptions)
        app.UseGlobalExceptionHandler();
        
        app.UseStatusCodePages();

        // 2. Request logging (logs all HTTP requests/responses)
        // app.UseRequestLogging();

        // 3. Development-only middleware
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference(options =>
            {
                options.Title = "A2I Stripe Subscription API";
                options.Theme = ScalarTheme.Purple;
            });
        }

        // 4. Hangfire dashboard
        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            // Authorization = new[] { new HangfireAuthorizationFilter() }
            DashboardTitle = "A2I Background Jobs"
        });

        // 5. HTTPS redirection
        app.UseHttpsRedirection();

        // 6. Authorization
        app.UseAuthorization();

        // ===== API ENDPOINTS =====

        // Legacy controller endpoints (keep for now)
        app.MapControllers();
        
        app.MapGroup("/")
            .MapJwksEndpoints();
        // ===== API v1 ENDPOINTS =====

        var apiV1 = app.MapGroup("/api/v1")
            .WithOpenApi();

        

        // System endpoints
        apiV1.MapGroup("/health")
            .WithTags("System")
            .MapHealthEndpoints();

        apiV1.MapGroup("/subscriptions")
            .WithTags("Subscriptions")
            .MapSubscriptionEndpoints();

        apiV1.MapGroup("/customers")
            .WithTags("Customers")
            .MapCustomerEndpoints();

        apiV1.MapGroup("/invoices")
            .WithTags("Invoices")
            .MapInvoiceEndpoints();
        
        apiV1.MapGroup("/auth")
            .WithTags("Auth")
            .MapAuthEndpoints();


        // Test endpoints (REMOVE IN PRODUCTION!)
        if (app.Environment.IsDevelopment())
            apiV1.MapGroup("/test")
                .WithTags("Test")
                .MapTestEndpoints();

        // Business endpoints (will be added in Phase 2)
        // apiV1.MapGroup("/subscriptions").WithTags("Subscriptions").MapSubscriptionEndpoints();
        // apiV1.MapGroup("/customers").WithTags("Customers").MapCustomerEndpoints();
        // apiV1.MapGroup("/invoices").WithTags("Invoices").MapInvoiceEndpoints();
        // apiV1.MapGroup("/plans").WithTags("Plans").MapPlanEndpoints();

        // ===== HEALTH CHECK ENDPOINT =====
        app.MapGet("/health", () => Results.Ok(new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                version = "1.0.0",
                environment = app.Environment.EnvironmentName
            }))
            .WithName("HealthCheck")
            .WithTags("System")
            .Produces(StatusCodes.Status200OK)
            .ExcludeFromDescription(); // Don't show in API docs

        // ===== DEMO ENDPOINT (remove in production) =====
        app.MapGet("/weatherforecast", (HttpContext httpContext) =>
            {
                var summaries = new[]
                {
                    "Freezing", "Bracing", "Chilly", "Cool", "Mild",
                    "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
                };

                var forecast = Enumerable.Range(1, 5).Select(index =>
                        new WeatherForecast
                        {
                            Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                            TemperatureC = Random.Shared.Next(-20, 55),
                            Summary = summaries[Random.Shared.Next(summaries.Length)]
                        })
                    .ToArray();
                return forecast;
            })
            .WithName("GetWeatherForecast")
            .WithTags("Demo");

        // ===== START APPLICATION =====
        app.Run();
    }
}