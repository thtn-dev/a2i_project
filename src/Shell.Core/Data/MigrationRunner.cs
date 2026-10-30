using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Shell.Abstractions;
using Shell.Abstractions.Tenancy;
using Shell.Core.Tenancy;

namespace Shell.Core.Data;

/// <summary>
/// The <c>migrate</c> command (never run at application start-up):
/// catalog DB → demo seed (optional) → <see cref="IMigrationStep"/>s (platform data in the catalog DB, e.g. identity)
/// → every module DbContext on the shared DB → the same on each Silo tenant DB.
/// A PostgreSQL advisory lock on the catalog DB makes concurrent runs wait for each other.
/// </summary>
public sealed class MigrationRunner(
    IOptions<ConnectionStringsOptions> connectionStrings,
    IOptions<TenancyOptions> tenancy,
    IEnumerable<ModuleDbContextRegistration> contexts,
    IEnumerable<IMigrationStep> steps,
    IServiceScopeFactory scopeFactory,
    ILogger<MigrationRunner> logger)
{
    /// <summary>Arbitrary application-wide key ("SHELLMIG" in ASCII).</summary>
    public const long AdvisoryLockKey = 0x5348454C4C4D4947;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await EnsureCatalogDatabaseExistsAsync(cancellationToken);

        // Session-level lock: held by this connection until unlocked or the connection closes.
        await using var lockConnection = new NpgsqlConnection(connectionStrings.Value.Catalog);
        await lockConnection.OpenAsync(cancellationToken);
        logger.LogInformation("Acquiring migration lock {LockKey}", AdvisoryLockKey);
        await ExecuteAsync(lockConnection, "SELECT pg_advisory_lock(@key)", cancellationToken);

        try
        {
            await MigrateCatalogAsync(cancellationToken);

            foreach (var step in steps)
            {
                logger.LogInformation("Running migration step {Step}", step.GetType().Name);
                await step.RunAsync(cancellationToken);
            }

            await MigrateModulesAsync(tenant: null, "shared database", cancellationToken);

            foreach (var tenant in await GetIsolatedTenantsAsync(cancellationToken))
            {
                await MigrateModulesAsync(tenant, $"database of tenant '{tenant.Identifier}'", cancellationToken);
            }

            logger.LogInformation("Migrations completed");
        }
        finally
        {
            await ExecuteAsync(lockConnection, "SELECT pg_advisory_unlock(@key)", CancellationToken.None);
        }
    }

    /// <summary>The lock lives in the catalog DB, so that DB must exist before anything else.</summary>
    private async Task EnsureCatalogDatabaseExistsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var creator = scope.ServiceProvider.GetRequiredService<CatalogDbContext>().GetService<IRelationalDatabaseCreator>();
        if (await creator.ExistsAsync(cancellationToken))
        {
            return;
        }

        logger.LogInformation("Creating catalog database");
        try
        {
            await creator.CreateAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DuplicateDatabase)
        {
            // Created by a concurrent migrate run.
        }
    }

    private async Task MigrateCatalogAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        logger.LogInformation("Migrating catalog database");
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync(cancellationToken);

        if (tenancy.Value.SeedDemoTenants)
        {
            await scope.ServiceProvider.GetRequiredService<DemoTenantSeeder>().SeedAsync(cancellationToken);
        }
    }

    private async Task<List<TenantInfo>> GetIsolatedTenantsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Tenants
            .AsNoTracking()
            .Where(t => t.IsIsolatedDb)
            .OrderBy(t => t.Name)
            .Select(t => new TenantInfo(t.Id, t.Name, t.IsIsolatedDb, t.ConnectionString, t.IsActive))
            .ToListAsync(cancellationToken);
    }

    /// <param name="tenant">Null: shared DB. Otherwise the module DbContexts connect to this tenant's DB.</param>
    private async Task MigrateModulesAsync(TenantInfo? tenant, string target, CancellationToken cancellationToken)
    {
        foreach (var registration in contexts)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            if (tenant is not null)
            {
                scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant);
            }

            var db = (DbContext)scope.ServiceProvider.GetRequiredService(registration.ContextType);
            logger.LogInformation("Migrating {Context} on {Target}", registration.ContextType.Name, target);
            await db.Database.MigrateAsync(cancellationToken); // Creates the database if it does not exist.
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("key", AdvisoryLockKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>
/// Extra work of the migrate command, run inside the migration lock right after the catalog DB (and demo tenants) are ready.
/// Lets platform components (e.g. Shell.Identity) migrate and seed their own data without Shell.Core depending on them.
/// </summary>
public interface IMigrationStep
{
    Task RunAsync(CancellationToken cancellationToken);
}
