using A2I.Application.Common.Caching;
using A2I.Application.Customers;
using A2I.Application.Invoices;
using A2I.Application.Notifications;
using A2I.Application.StripeAbstraction;
using A2I.Application.StripeAbstraction.Checkout;
using A2I.Application.StripeAbstraction.Customers;
using A2I.Application.StripeAbstraction.Portal;
using A2I.Application.StripeAbstraction.Subscriptions;
using A2I.Application.StripeAbstraction.Webhooks;
using A2I.Application.Subscriptions;
using A2I.Infrastructure.Caching;
using A2I.Infrastructure.Caching.Providers;
using A2I.Infrastructure.Customers;
using A2I.Infrastructure.Database;
using A2I.Infrastructure.Identity;
using A2I.Infrastructure.Identity.Entities;
using A2I.Infrastructure.Identity.Security;
using A2I.Infrastructure.Invoices;
using A2I.Infrastructure.Notifications;
using A2I.Infrastructure.StripeServices;
using A2I.Infrastructure.StripeServices.WebhookHandlers;
using A2I.Infrastructure.Subscriptions;
using A2I.WebAPI.BackgroundJobs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Quartz;
using StackExchange.Redis;

namespace A2I.WebAPI.Extensions;

public static class ServiceCollectionExtensions
{
    public static void AddBackgroundJobServices(this IServiceCollection services)
    {
        services.AddQuartz(q =>
        {
            var jobKey = new JobKey("KeyRotationJob");
            q.AddJob<KeyRotationJob>(opts => opts.WithIdentity(jobKey));

            q.AddTrigger(opts => opts
                .ForJob(jobKey)
                .WithIdentity("KeyRotationTrigger")
                .WithCronSchedule("0 0 2 */30 * ?")); // 2 AM mỗi 30 ngày
            // .WithSimpleSchedule(x => x.WithIntervalInHours(24).RepeatForever())); // Test: mỗi 24 giờ
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
    }
    public static void AddIdentityServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtSettings>(
            configuration.GetSection(JwtSettings.SectionName));
        services.AddSingleton<IJwtService, JwtService>();
        services.AddSingleton<IKeyManagementService, KeyManagementService>();
        
        services.AddDbContextPool<AppIdentityDbContext>((serviceProvider, options) =>
        {
            var dbOptions = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            var connectionString = BuildConnectionString(dbOptions);

            options.UseNpgsql(connectionString, npgsqlOptions =>
                {
                    npgsqlOptions.EnableRetryOnFailure(
                        3,
                        TimeSpan.FromSeconds(10),
                        null);

                    npgsqlOptions.CommandTimeout(30);
                    npgsqlOptions.MigrationsAssembly(typeof(AppIdentityDbContext).Assembly.FullName);
                    npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "identity");
                })
                .UseSnakeCaseNamingConvention();
        });
        
        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                // password
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 6;

                // lockout
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;

                // User
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddEntityFrameworkStores<AppIdentityDbContext>()
            .AddDefaultTokenProviders();
        
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var jwtSettings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>();
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings?.Issuer,
                    ValidAudience = jwtSettings?.Audience,
                    IssuerSigningKeyResolver = (_, _, _, _) =>
                    {
                        var keyService = services.BuildServiceProvider()
                            .GetRequiredService<KeyManagementService>();
                        return keyService.GetAllPublicKeys();
                    }
                };
            });
    }
    
    public static void AddDatabaseServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        var databaseSection = configuration.GetSection(DatabaseOptions.SectionName);
        services.Configure<DatabaseOptions>(databaseSection);

        services.AddDbContextPool<ApplicationDbContext>((serviceProvider, options) =>
        {
            var dbOptions = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            var connectionString = BuildConnectionString(dbOptions);

            // var loggerService = serviceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();
            options.UseNpgsql(connectionString, npgsqlOptions =>
                {
                    npgsqlOptions.EnableRetryOnFailure(
                        3,
                        TimeSpan.FromSeconds(10),
                        null);

                    npgsqlOptions.CommandTimeout(30);
                    npgsqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                })
                .UseSnakeCaseNamingConvention();

            if (environment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging(dbOptions.EnableSensitiveDataLogging);
                options.EnableDetailedErrors(dbOptions.EnableDetailedErrors);
                // options.LogTo(Console.WriteLine, LogLevel.Information);
            }
            // else
            // {
            //     options.LogTo(
            //         filter: (_, level) => level >= LogLevel.Warning,
            //         logger: (eventData) => loggerService.LogWarning("Database event: {EventData}", eventData.ToString()));
            // }
        });
    }

    public static string BuildConnectionString(DatabaseOptions dbOptions)
    {
        var builder = new NpgsqlConnectionStringBuilder(dbOptions.ConnectionString)
        {
            MinPoolSize = dbOptions.ConnectionPool.MinPoolSize,
            MaxPoolSize = dbOptions.ConnectionPool.MaxPoolSize,
            ConnectionIdleLifetime = dbOptions.ConnectionPool.ConnectionIdleLifetime,
            Timeout = dbOptions.ConnectionPool.ConnectionTimeout,
            Multiplexing = false,
            TcpKeepAlive = true,
            TcpKeepAliveTime = 600,
            TcpKeepAliveInterval = 30,
            ApplicationName = "WebAPI",
            MaxAutoPrepare = 0
        };

        return builder.ConnectionString;
    }

    public static void AddStripeServices(this IServiceCollection services, IConfiguration configuration)
    {
        var stripeSection = configuration.GetSection(StripeOptions.SectionName);
        services.Configure<StripeOptions>(stripeSection);
        services.AddScoped<IStripeCheckoutService, StripeCheckoutService>();
        services.AddScoped<IStripeCustomerService, StripeCustomerService>();
        services.AddScoped<IStripePortalService, StripePortalService>();
        services.AddScoped<IStripeSubscriptionService, StripeSubscriptionService>();

        services.AddScoped<ICustomerApplicationService, CustomerApplicationService>();
        services.AddScoped<IInvoiceApplicationService, InvoiceApplicationService>();
        services.AddScoped<IEmailService, MockEmailService>();
        services.AddScoped<ISubscriptionApplicationService, SubscriptionApplicationService>();
        services.AddScoped<IEventIdempotencyStore, DbEventIdempotencyStore>();
        services.AddScoped<IWebhookEventDispatcher, WebhookEventDispatcher>();

        // Register all webhook handlers
        services.AddScoped<IWebhookEventHandler, CheckoutSessionCompletedHandler>();
        
        services.AddScoped<IWebhookEventHandler, CustomerCreatedHandler>();
        services.AddScoped<IWebhookEventHandler, CustomerDeletedHandler>();
        services.AddScoped<IWebhookEventHandler, CustomerUpdatedHandler>();
        
        services.AddScoped<IWebhookEventHandler, InvoiceCreatedHandler>();
        services.AddScoped<IWebhookEventHandler, InvoiceFinalizedHandler>();
        services.AddScoped<IWebhookEventHandler, InvoicePaidHandler>();
        services.AddScoped<IWebhookEventHandler, InvoicePaymentActionRequiredHandler>();
        services.AddScoped<IWebhookEventHandler, InvoicePaymentFailedHandler>();
        services.AddScoped<IWebhookEventHandler, InvoiceVoidedHandler>();
        
        services.AddScoped<IWebhookEventHandler, SubscriptionCreatedHandler>();
        services.AddScoped<IWebhookEventHandler, SubscriptionDeletedHandler>();
        services.AddScoped<IWebhookEventHandler, SubscriptionTrialWillEndHandler>();
        services.AddScoped<IWebhookEventHandler, SubscriptionUpdatedHandler>();
    }
    
    public static void AddCacheService(
        this IServiceCollection services, 
        Action<CacheOptions> configureOptions)
    {
        var options = new CacheOptions();
        configureOptions(options);
        services.AddSingleton(options);

        if (options.CacheType == CacheType.Redis)
        {
            if (string.IsNullOrEmpty(options.ConnectionString))
                throw new ArgumentException("Redis connection string is required for Redis cache type.");

            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(options.ConnectionString));

            services.AddSingleton<ICacheService, RedisCacheService>();
        }
        else
        {
            throw new NotImplementedException("Memory cache is not implemented yet.");
        }
    }
}