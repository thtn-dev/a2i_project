using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Shell.Abstractions;
using Shell.Abstractions.Tenancy;
using Shell.Core.Data;
using Shell.Core.Modules;
using Shell.Core.Tenancy;

namespace Shell.Core;

public static class ShellServiceCollectionExtensions
{
    private const string ReadyTag = "ready";

    /// <summary>Registers the shell services (options, tenancy, migrations, health) and then every module's services, in load order.</summary>
    public static IServiceCollection AddShell(this IServiceCollection services, IConfiguration configuration, ModuleCatalog catalog)
    {
        services.AddOptions<ShellOptions>()
            .BindConfiguration(ShellOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<ConnectionStringsOptions>()
            .BindConfiguration(ConnectionStringsOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(o => ConnectionStringsOptions.IsValid(o.Catalog), "ConnectionStrings:Catalog is not a valid PostgreSQL connection string.")
            .Validate(o => ConnectionStringsOptions.IsValid(o.DefaultShared), "ConnectionStrings:DefaultShared is not a valid PostgreSQL connection string.")
            .ValidateOnStart();
        services.AddOptions<TenancyOptions>()
            .BindConfiguration(TenancyOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddSingleton(catalog);

        // Tenancy
        services.AddDbContext<CatalogDbContext>((sp, options) => options.UseNpgsql(
            sp.GetRequiredService<IOptions<ConnectionStringsOptions>>().Value.Catalog,
            npgsql => npgsql.MigrationsHistoryTable(ModuleDbContextExtensions.MigrationsHistoryTable, CatalogDbContext.Schema)));
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantConnectionStringProvider, TenantConnectionStringProvider>();
        services.AddScoped<TenantStore>();
        services.AddScoped<DemoTenantSeeder>();

        services.AddTransient<MigrationRunner>();
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag], timeout: TimeSpan.FromSeconds(5));

        foreach (var module in catalog.Modules)
        {
            module.ConfigureServices(services, configuration);
        }

        return services;
    }

    /// <summary>Resolves the tenant of each request (404 unknown, 403 locked). Place before the endpoints.</summary>
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantResolutionMiddleware>();

    /// <summary><c>/health/live</c>: process is up (no dependency checked). <c>/health/ready</c>: catalog and shared DB reachable.</summary>
    public static IEndpointRouteBuilder MapShellHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) });
        return endpoints;
    }

    /// <summary>Maps each module's endpoints under <c>/api/{moduleId}</c>.</summary>
    public static IEndpointRouteBuilder MapModules(this IEndpointRouteBuilder endpoints)
    {
        var catalog = endpoints.ServiceProvider.GetRequiredService<ModuleCatalog>();
        foreach (var module in catalog.Modules)
        {
            module.MapEndpoints(endpoints.MapGroup($"/api/{module.Id}"));
        }

        return endpoints;
    }
}
