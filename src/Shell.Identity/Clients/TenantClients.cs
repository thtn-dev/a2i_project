using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using Shell.Core.Tenancy;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Shell.Identity.Clients;

/// <summary>
/// One public OAuth client per tenant (<c>tenant-{tenantId}</c>), allowed to redirect only to that tenant's domains.
/// The client a request comes from therefore tells which tenant the token is for.
/// </summary>
public static class TenantClients
{
    private const string Prefix = "tenant-";

    /// <summary>Scope granting access to the tenant APIs; tokens carrying it have the <see cref="ApiAudience"/> audience.</summary>
    public const string ApiScope = "api";

    public const string ApiAudience = "shell-api";

    public static string ClientIdFor(Guid tenantId) => $"{Prefix}{tenantId}";

    public static Guid? TenantIdOf(string? clientId) =>
        clientId is not null && clientId.StartsWith(Prefix, StringComparison.Ordinal) && Guid.TryParse(clientId.AsSpan(Prefix.Length), out var id)
            ? id
            : null;
}

/// <summary>Creates or updates the OAuth client of every tenant from the catalog (run by the migrate command).</summary>
public sealed class TenantClientSynchronizer(
    CatalogDbContext catalog,
    IOpenIddictApplicationManager applications,
    IOptions<ShellIdentityOptions> options,
    ILogger<TenantClientSynchronizer> logger)
{
    public async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        var tenants = await catalog.Tenants.AsNoTracking().Include(t => t.Domains).ToListAsync(cancellationToken);
        foreach (var tenant in tenants)
        {
            var descriptor = CreateDescriptor(tenant);
            var existing = await applications.FindByClientIdAsync(descriptor.ClientId!, cancellationToken);
            if (existing is null)
            {
                await applications.CreateAsync(descriptor, cancellationToken);
                logger.LogInformation("Created OAuth client {ClientId} for tenant {Tenant}", descriptor.ClientId, tenant.Name);
            }
            else
            {
                await applications.UpdateAsync(existing, descriptor, cancellationToken);
            }
        }
    }

    private OpenIddictApplicationDescriptor CreateDescriptor(Tenant tenant)
    {
        var clients = options.Value.Clients;
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = TenantClients.ClientIdFor(tenant.Id),
            ClientType = ClientTypes.Public,
            ApplicationType = ApplicationTypes.Web,
            ConsentType = ConsentTypes.Implicit, // First-party applications: no consent screen.
            DisplayName = tenant.Name,
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Email,
                Permissions.Prefixes.Scope + TenantClients.ApiScope,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };

        foreach (var domain in tenant.Domains.Select(d => d.Domain))
        {
            foreach (var template in clients.RedirectUris)
            {
                descriptor.RedirectUris.Add(new Uri(template.Replace("{domain}", domain, StringComparison.Ordinal)));
            }

            foreach (var template in clients.PostLogoutRedirectUris)
            {
                descriptor.PostLogoutRedirectUris.Add(new Uri(template.Replace("{domain}", domain, StringComparison.Ordinal)));
            }
        }

        return descriptor;
    }
}
