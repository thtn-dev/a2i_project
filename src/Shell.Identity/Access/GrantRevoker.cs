using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using Shell.Identity.Clients;
using Shell.Identity.Data;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Shell.Identity.Access;

/// <summary>
/// Revokes the OAuth grants (authorizations, refresh tokens, unused codes) of a user, for one tenant's client or for all.
/// Goes through the OpenIddict managers entity by entity so their caches stay consistent.
/// Access tokens are self-contained: they stop working through the per-request membership check instead.
/// </summary>
public sealed class GrantRevoker(AuthDbContext db, IOpenIddictAuthorizationManager authorizations, IOpenIddictTokenManager tokens)
{
    /// <param name="tenantId">Null: every tenant.</param>
    public async Task RevokeAsync(Guid userId, Guid? tenantId, CancellationToken cancellationToken)
    {
        var subject = userId.ToString();
        var clientId = tenantId is null ? null : TenantClients.ClientIdFor(tenantId.Value);

        var authorizationIds = await db.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>()
            .Where(a => a.Subject == subject && a.Status == Statuses.Valid && (clientId == null || a.Application!.ClientId == clientId))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in authorizationIds)
        {
            if (await authorizations.FindByIdAsync(id.ToString(), cancellationToken) is { } authorization)
            {
                await authorizations.TryRevokeAsync(authorization, cancellationToken);
            }
        }

        var tokenIds = await db.Set<OpenIddictEntityFrameworkCoreToken<Guid>>()
            .Where(t => t.Subject == subject && t.Status == Statuses.Valid && (clientId == null || t.Application!.ClientId == clientId))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in tokenIds)
        {
            if (await tokens.FindByIdAsync(id.ToString(), cancellationToken) is { } token)
            {
                await tokens.TryRevokeAsync(token, cancellationToken);
            }
        }
    }
}
