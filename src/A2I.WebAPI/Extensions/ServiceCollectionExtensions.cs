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
using A2I.Infrastructure.Customers;
using A2I.Infrastructure.Database;
using A2I.Infrastructure.Invoices;
using A2I.Infrastructure.Notifications;
using A2I.Infrastructure.StripeServices;
using A2I.Infrastructure.StripeServices.WebhookHandlers;
using A2I.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace A2I.WebAPI.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDatabaseServices(
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

            var loggerService = serviceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null);

                npgsqlOptions.CommandTimeout(30);
                npgsqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
            });

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

        return services;
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
            MaxAutoPrepare = 0,
        };
        
        return builder.ConnectionString;
    }

    public static IServiceCollection AddStripeServices(this IServiceCollection services, IConfiguration configuration)
    {
        var stripeSection = configuration.GetSection(StripeOptions.SectionName);
        services.Configure<StripeOptions>(stripeSection);
        services.AddScoped<IStripeCheckoutService, StripeCheckoutService>();
        services.AddScoped<IStripeCustomerService, StripeCustomerService>();
        services.AddScoped<IStripePortalService, StripePortalService>();
        services.AddScoped<IStripeSubscriptionService, StripeSubscriptionService>();
        services.AddScoped<IStripeWebhookService, StripeWebhookService>();
        
        services.AddScoped<ICustomerApplicationService, CustomerApplicationService>();
        services.AddScoped<IInvoiceApplicationService, InvoiceApplicationService>();
        services.AddScoped<IEmailService, MockEmailService>();
        services.AddScoped<ISubscriptionApplicationService, SubscriptionApplicationService>();
        services.AddScoped<IEventIdempotencyStore, DbEventIdempotencyStore>();
        
        // Register all webhook handlers
        services.AddScoped<IWebhookEventHandler, CheckoutSessionCompletedHandler>();
        services.AddScoped<IWebhookEventHandler, SubscriptionCreatedHandler>();
        services.AddScoped<IWebhookEventHandler, InvoicePaidHandler>();
        services.AddScoped<IWebhookEventHandler, InvoicePaymentFailedHandler>();
        services.AddScoped<IWebhookEventHandler, SubscriptionUpdatedHandler>();
        services.AddScoped<IWebhookEventHandler, SubscriptionDeletedHandler>();
        services.AddScoped<IWebhookEventHandler, CustomerCreatedHandler>();
        services.AddScoped<IWebhookEventDispatcher, WebhookEventDispatcher>();
        return services;
    }
}