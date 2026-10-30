using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Shell.Abstractions.Tenancy;
using Shell.Core.Tenancy;
using Shell.Identity.Access;
using Shell.Identity.Audit;
using Shell.Identity.Clients;
using Shell.Identity.Data;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Shell.Identity.Endpoints;

/// <summary>Claim types added by the shell.</summary>
public static class ShellClaims
{
    /// <summary>Id of the only tenant the token is valid for.</summary>
    public const string TenantId = "tid";
}

/// <summary>
/// OpenID Connect endpoints of the authorization server (OpenIddict passthrough). A token is always issued for exactly one tenant:
/// the tenant owning the client, and only to an active member of it.
/// </summary>
internal static class AuthorizationEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], AuthorizeAsync);
        endpoints.MapPost("/connect/token", ExchangeAsync);
        endpoints.MapMethods("/connect/endsession", [HttpMethods.Get, HttpMethods.Post], (Delegate)EndSessionAsync);
        endpoints.MapMethods("/connect/userinfo", [HttpMethods.Get, HttpMethods.Post], UserInfoAsync);
    }

    private static async Task<IResult> AuthorizeAsync(
        HttpContext context, TenantStore tenants, TenantAccessService access, UserManager<User> users, AuditLog audit, ILoggerFactory loggers)
    {
        var request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Not an OpenID Connect request.");
        var tenant = await FindTenantAsync(tenants, request.ClientId, context.RequestAborted);
        if (tenant is null)
        {
            return Deny(Errors.AccessDenied, "This tenant is unavailable.");
        }

        var session = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!session.Succeeded)
        {
            return request.HasPromptValue(PromptValues.None)
                ? Deny(Errors.LoginRequired, "The user is not signed in.")
                : Results.Challenge(new AuthenticationProperties { RedirectUri = context.Request.PathBase + context.Request.Path + QueryString.Create(
                    context.Request.HasFormContentType ? context.Request.Form : context.Request.Query) }, [IdentityConstants.ApplicationScheme]);
        }

        var user = await users.GetUserAsync(session.Principal);
        if (user is null || user.IsDisabled)
        {
            return Deny(Errors.AccessDenied, "The account is disabled.");
        }

        var membership = await access.CheckAsync(tenant.Id, user.Id, context.RequestAborted);
        if (membership is null)
        {
            loggers.CreateLogger(typeof(AuthorizationEndpoints)).LogWarning(
                "Authorization denied: user {UserId} has no active access to tenant {TenantId}", user.Id, tenant.Id);
            await audit.RecordAsync(AuditEvents.AuthorizationDenied, user.Id, tenant.Id);
            return Deny(Errors.AccessDenied, "You are not a member of this tenant.");
        }

        var methods = session.Principal.FindAll("amr").Select(c => c.Value);
        var identity = CreateIdentity(user, tenant, membership, request.GetScopes(), methods);
        return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>Code and refresh token grants. Access is checked again, so a revoked member cannot refresh.</summary>
    private static async Task<IResult> ExchangeAsync(HttpContext context, TenantStore tenants, TenantAccessService access, UserManager<User> users)
    {
        var request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Not an OpenID Connect request.");
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            return Deny(Errors.UnsupportedGrantType, "Unsupported grant type.");
        }

        var principal = (await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;
        var tenantId = Guid.TryParse(principal?.GetClaim(ShellClaims.TenantId), out var tid) ? tid : (Guid?)null;
        var tenant = tenantId is null ? null : await tenants.FindByIdAsync(tenantId.Value, context.RequestAborted);
        var user = principal?.GetClaim(Claims.Subject) is { } subject ? await users.FindByIdAsync(subject) : null;

        // The client must still be the tenant's own client (also enforced by OpenIddict: grants are bound to their client).
        if (tenant is not { IsActive: true } || TenantClients.TenantIdOf(request.ClientId) != tenant.Id || user is null || user.IsDisabled)
        {
            return Deny(Errors.InvalidGrant, "The grant is no longer valid.");
        }

        var membership = await access.CheckAsync(tenant.Id, user.Id, context.RequestAborted);
        if (membership is null)
        {
            return Deny(Errors.InvalidGrant, "The user is no longer a member of this tenant.");
        }

        var identity = CreateIdentity(user, tenant, membership, principal!.GetScopes(), principal!.GetClaims("amr"));
        return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> EndSessionAsync(HttpContext context)
    {
        await context.SignOutAsync(IdentityConstants.ApplicationScheme);
        return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    private static async Task<IResult> UserInfoAsync(HttpContext context, UserManager<User> users)
    {
        var principal = (await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;
        var user = principal?.GetClaim(Claims.Subject) is { } subject ? await users.FindByIdAsync(subject) : null;
        if (user is null || user.IsDisabled)
        {
            return Results.Challenge(Error(Errors.InvalidToken, "The account is unavailable."), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [Claims.Subject] = user.Id.ToString(),
            [ShellClaims.TenantId] = principal!.GetClaim(ShellClaims.TenantId)!,
        };
        if (principal!.HasScope(Scopes.Email))
        {
            claims[Claims.Email] = user.Email!;
            claims[Claims.EmailVerified] = user.EmailConfirmed;
        }

        if (principal!.HasScope(Scopes.Profile))
        {
            claims[Claims.PreferredUsername] = user.UserName!;
        }

        return Results.Ok(claims);
    }

    private static async Task<TenantInfo?> FindTenantAsync(TenantStore tenants, string? clientId, CancellationToken cancellationToken) =>
        TenantClients.TenantIdOf(clientId) is { } tenantId && await tenants.FindByIdAsync(tenantId, cancellationToken) is { IsActive: true } tenant
            ? tenant
            : null;

    /// <summary>Claims of a token for one tenant. The user id (never the email) is the subject.</summary>
    private static ClaimsIdentity CreateIdentity(
        User user, TenantInfo tenant, TenantAccess membership, IEnumerable<string> scopes, IEnumerable<string> methods)
    {
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id.ToString())
            .SetClaim(Claims.Name, user.UserName)
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(ShellClaims.TenantId, tenant.Id.ToString())
            .SetClaim(Claims.Role, membership.Role)
            .SetClaims("amr", [.. methods.Distinct()]);

        identity.SetScopes(scopes.Intersect([Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.OfflineAccess, TenantClients.ApiScope]));
        identity.SetResources(TenantClients.ApiAudience);
        identity.SetDestinations(claim => claim.Type switch
        {
            Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],
            ShellClaims.TenantId or "amr" => [Destinations.AccessToken, Destinations.IdentityToken],
            Claims.Role => [Destinations.AccessToken],
            Claims.Name when claim.Subject!.HasScope(Scopes.Profile) => [Destinations.IdentityToken],
            Claims.Email when claim.Subject!.HasScope(Scopes.Email) => [Destinations.IdentityToken],
            _ => [],
        });
        return identity;
    }

    private static IResult Deny(string error, string description) =>
        Results.Forbid(Error(error, description), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

    private static AuthenticationProperties Error(string error, string description) => new(new Dictionary<string, string?>
    {
        [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
    });
}
