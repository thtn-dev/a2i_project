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

        var ttl = tenant is null
            ? TimeSpan.FromSeconds(options.Value.NotFoundCacheSeconds)
            : TimeSpan.FromMinutes(options.Value.CacheMinutes);
        cache.Set(key, new CachedTenant(tenant), ttl);

        return tenant;
    }

    private sealed record CachedTenant(TenantInfo? Tenant);
}
