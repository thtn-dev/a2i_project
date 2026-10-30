using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Shell.Abstractions.Tenancy;

namespace Shell.Core.Tenancy;

/// <summary>
/// Finds the tenant of a domain: in-memory cache first, catalog DB on a miss.
/// Unknown domains are cached too (negative caching) so they cannot hammer the catalog.
/// </summary>
public sealed class TenantStore(CatalogDbContext catalog, IMemoryCache cache, IOptions<TenancyOptions> options)
{
    /// <param name="domain">Normalised (lowercase, no port) host name.</param>
    public async Task<TenantInfo?> FindByDomainAsync(string domain, CancellationToken cancellationToken)
    {
        var key = $"tenant:{domain}";
        if (cache.TryGetValue(key, out CachedTenant? cached))
        {
            return cached!.Tenant;
        }

        var tenant = await catalog.TenantDomains
            .AsNoTracking()
            .Where(d => d.Domain == domain)
            .Select(d => new TenantInfo(d.Tenant.Id, d.Domain, d.Tenant.IsIsolatedDb, d.Tenant.ConnectionString, d.Tenant.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

        Cache(key, tenant);
        return tenant;
    }

    /// <summary>Same as <see cref="FindByDomainAsync"/> but by id (e.g. the tenant a token or OAuth client belongs to).</summary>
    public async Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var key = $"tenant-id:{id}";
        if (cache.TryGetValue(key, out CachedTenant? cached))
        {
            return cached!.Tenant;
        }

        var tenant = await catalog.Tenants
            .AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new TenantInfo(t.Id, t.Name, t.IsIsolatedDb, t.ConnectionString, t.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

        Cache(key, tenant);
        return tenant;
    }

    private void Cache(string key, TenantInfo? tenant)
    {
        var ttl = tenant is null
            ? TimeSpan.FromSeconds(options.Value.NotFoundCacheSeconds)
            : TimeSpan.FromMinutes(options.Value.CacheMinutes);
        cache.Set(key, new CachedTenant(tenant), ttl);
    }

    private sealed record CachedTenant(TenantInfo? Tenant);
}
