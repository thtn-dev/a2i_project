using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Shell.Core.Tenancy;

/// <summary>
/// Creates two demo tenants when the catalog is empty (run by the migrate command when
/// <see cref="TenancyOptions.SeedDemoTenants"/> is true):
/// <c>tenant-a.localhost</c> on the shared DB (Pool) and <c>tenant-b.localhost</c> on its own DB (Silo).
/// </summary>
public sealed class DemoTenantSeeder(CatalogDbContext catalog, IOptions<ConnectionStringsOptions> connectionStrings, ILogger<DemoTenantSeeder> logger)
{
    public const string PoolTenantDomain = "tenant-a.localhost";
    public const string SiloTenantDomain = "tenant-b.localhost";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await catalog.Tenants.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Catalog already has tenants, demo seeding skipped");
            return;
        }

        // Same server and credentials as the shared DB, separate database "<shared>_tenant_b".
        var silo = new NpgsqlConnectionStringBuilder(connectionStrings.Value.DefaultShared);
        silo.Database = $"{silo.Database}_tenant_b";

        catalog.Tenants.AddRange(
            new Tenant
            {
                Id = Guid.CreateVersion7(),
                Name = "Tenant A (Pool)",
                Domains = [new TenantDomain { Id = Guid.CreateVersion7(), Domain = PoolTenantDomain }],
            },
            new Tenant
            {
                Id = Guid.CreateVersion7(),
                Name = "Tenant B (Silo)",
                IsIsolatedDb = true,
                ConnectionString = silo.ConnectionString,
                Domains = [new TenantDomain { Id = Guid.CreateVersion7(), Domain = SiloTenantDomain }],
            });
        await catalog.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded demo tenants {PoolDomain} (shared DB) and {SiloDomain} (database {SiloDatabase})",
            PoolTenantDomain, SiloTenantDomain, silo.Database);
    }
}
