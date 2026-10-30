using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shell.Abstractions.Tenancy;

namespace Shell.Abstractions;

/// <summary>A module DbContext that the <c>migrate</c> command migrates on the shared DB and on every tenant's own DB.</summary>
public sealed record ModuleDbContextRegistration(Type ContextType);

public static class ModuleDbContextExtensions
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>
    /// Registers a module DbContext (usually a <see cref="TenantDbContext"/>) that connects, per scope,
    /// to the current tenant's database: its own DB (Silo) or the shared DB (Pool).
    /// Its migration history lives in the module's own <paramref name="schema"/>; the DbContext must call
    /// <c>HasDefaultSchema(schema)</c>.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : DbContext
    {
        // A connection string (not a shared NpgsqlDataSource) so that each tenant DB gets its own Npgsql pool.
        services.AddDbContext<TContext>((sp, options) => options.UseNpgsql(
            sp.GetRequiredService<ITenantConnectionStringProvider>().GetConnectionString(),
            npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, schema)));
        services.AddSingleton(new ModuleDbContextRegistration(typeof(TContext)));
        return services;
    }

    /// <summary>
    /// Options for <c>IDesignTimeDbContextFactory</c> (used by <c>dotnet ef migrations add</c>, no database needed).
    /// </summary>
    public static DbContextOptions<TContext> CreateDesignTimeOptions<TContext>(string schema)
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseNpgsql("Host=localhost;Database=design_time", npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, schema))
            .Options;

    /// <summary>Tenant context with no tenant, for design-time factories.</summary>
    public static ITenantContext DesignTimeTenantContext { get; } = new NoTenantContext();

    private sealed class NoTenantContext : ITenantContext
    {
        public TenantInfo? Current => null;
    }
}
