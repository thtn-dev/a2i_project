using A2I.Infrastructure.Database;
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
                options.LogTo(Console.WriteLine, LogLevel.Information);
            }
            else
            {
                options.LogTo(
                    filter: (eventId, level) => level >= LogLevel.Warning,
                    logger: (eventData) => loggerService.LogWarning("Database event: {EventData}", eventData.ToString()));
            }
        });

        return services;
    }

    private static string BuildConnectionString(DatabaseOptions dbOptions)
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
            MaxAutoPrepare = 100,
        };

        return builder.ConnectionString;
    }
}