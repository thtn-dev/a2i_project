using System.Threading.RateLimiting;
using A2I.Infrastructure.Caching;
using A2I.Infrastructure.Database;
using A2I.Infrastructure.StripeServices;
using A2I.WebAPI.Endpoints;
using A2I.WebAPI.Endpoints.Auth;
using A2I.WebAPI.Extensions;
using A2I.WebAPI.Middlewares;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.RateLimiting;
using Scalar.AspNetCore;

namespace A2I.WebAPI;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args)
            .RegisterServices();

        var app = builder.Build()
            .RegisterPipelines();
        
        await app.RunAsync();
    }
    
    private static WebApplication RegisterPipelines(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseRateLimiter();
        }
        app.UseGlobalExceptionHandler();
        app.UseStatusCodePages();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference(options =>
            {
                options.Title = "A2I Stripe Subscription API";
                options.Theme = ScalarTheme.Purple;
            });
        }

        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            // Authorization = new[] { new HangfireAuthorizationFilter() }
            DashboardTitle = "A2I Background Jobs"
        });

        app.UseHttpsRedirection();
        app.UseAuthorization();

        // ===== API ENDPOINTS =====
        app.MapControllers();
        app.MapGroup("/")
            .MapJwksEndpoints();
        
        // ===== API v1 ENDPOINTS =====
        app.MapGroup("/api/v1")
            .WithOpenApi()
            .MapV1Endpoints();
        return app;
    }
    
    private static WebApplicationBuilder RegisterServices(this WebApplicationBuilder builder)
    {
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

            rateLimiterOptions.AddFixedWindowLimiter("login_fixed", options =>
            {
                options.PermitLimit = 3;
                options.Window = TimeSpan.FromMinutes(3);
                options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                options.QueueLimit = 0;
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
            options.ConnectionString = "redis://default:FcJ2dUSeLKqFXTQEb7TtagbGeWwhzwex@redis-17046.crce185.ap-seast-1-1.ec2.redns.redis-cloud.com:17046";
            options.DefaultExpirationMinutes = 30;
            options.InstanceName = "A2I_Cache_";
        });

        builder.Services.AddScoped<IStripeWebhookJob, StripeWebhookJob>();

        builder.Services.ConfigureOpenApi();
        return builder;
    }
}