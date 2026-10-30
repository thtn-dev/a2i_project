using Microsoft.Extensions.Options;
using Shell.Abstractions.Tenancy;

namespace Shell.Core.Tenancy;

/// <summary>Scoped holder of the current tenant. Only the shell sets it (middleware, migrate command).</summary>
public sealed class TenantContext : ITenantContext
{
    public TenantInfo? Current { get; private set; }

    public void Set(TenantInfo tenant)
    {
        if (Current is not null && Current.Id != tenant.Id)
        {
            throw new InvalidOperationException($"The tenant of this scope is already {Current.Id}; it cannot change to {tenant.Id}.");
        }

        Current = tenant;
    }
}

/// <summary>Silo tenant → its own database; Pool tenant or no tenant → the shared database.</summary>
internal sealed class TenantConnectionStringProvider(ITenantContext tenantContext, IOptions<ConnectionStringsOptions> connectionStrings)
    : ITenantConnectionStringProvider
{
    public string GetConnectionString()
    {
        var tenant = tenantContext.Current;
        if (tenant is not { IsIsolatedDb: true })
        {
            return connectionStrings.Value.DefaultShared;
        }

        // Never fall back to the shared DB for a Silo tenant: that would mix its data into the pool.
        return string.IsNullOrWhiteSpace(tenant.ConnectionString)
            ? throw new InvalidOperationException($"Tenant {tenant.Id} uses an isolated database but has no connection string.")
            : tenant.ConnectionString;
    }
}
