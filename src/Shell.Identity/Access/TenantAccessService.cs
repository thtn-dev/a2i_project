using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Shell.Identity.Data;

namespace Shell.Identity.Access;

/// <param name="Role">Role in the tenant (see <see cref="Shell.Abstractions.Security.TenantRoles"/>).</param>
public sealed record TenantAccess(string Role);

/// <summary>
/// Single rule deciding whether a user may access a tenant: enabled account, active membership, and a managed account
/// only in the tenant that manages it. Used when issuing tokens (uncached) and on every API request (cached briefly).
/// </summary>
public sealed class TenantAccessService(AuthDbContext db, IMemoryCache cache, IOptions<ShellIdentityOptions> options)
{
    /// <summary>Fresh answer from the database. Null: no access.</summary>
    public Task<TenantAccess?> CheckAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        db.Memberships
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.UserId == userId && m.Status == MembershipStatus.Active)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m.Role, User = u })
            .Where(x => !x.User.IsDisabled && (x.User.ManagedByTenantId == null || x.User.ManagedByTenantId == tenantId))
            .Select(x => new TenantAccess(x.Role))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Cached for <see cref="ShellIdentityOptions.MembershipCacheSeconds"/>: a revoked member keeps API access at most that long
    /// on other instances (immediately lost on the instance that called <see cref="Invalidate"/>).
    /// </summary>
    public async Task<TenantAccess?> GetCachedAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var key = CacheKey(tenantId, userId);
        if (cache.TryGetValue(key, out CachedAccess? cached))
        {
            return cached!.Access;
        }

        var access = await CheckAsync(tenantId, userId, cancellationToken);
        cache.Set(key, new CachedAccess(access), TimeSpan.FromSeconds(options.Value.MembershipCacheSeconds));
        return access;
    }

    public void Invalidate(Guid tenantId, Guid userId) => cache.Remove(CacheKey(tenantId, userId));

    private static string CacheKey(Guid tenantId, Guid userId) => $"membership:{tenantId}:{userId}";

    private sealed record CachedAccess(TenantAccess? Access);
}
