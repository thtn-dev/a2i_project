using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using Shell.Abstractions.Tenancy;
using Shell.Core.Security;
using Shell.Identity.Clients;
using Shell.Identity.Endpoints;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Shell.Identity.Access;

/// <summary>
/// Binds the bearer token to the tenant resolved from the host name. Runs after authentication, on tenant hosts only:
/// token of another tenant (<c>tid</c> or client) → 403; no longer an active member → 401; otherwise <see cref="CurrentUser"/>
/// is set with the member's current role. Requests without a valid token continue unauthenticated and are refused (401) by the
/// <see cref="Shell.Abstractions.Security.TenantPolicies"/> every tenant endpoint requires.
/// </summary>
internal sealed class TenantAccessMiddleware(RequestDelegate next, ILogger<TenantAccessMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, CurrentUser currentUser, TenantAccessService access)
    {
        var principal = context.User;
        if (tenantContext.Current is not { } tenant || principal.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var tokenTenant = principal.GetClaim(ShellClaims.TenantId);
        var clientTenant = TenantClients.TenantIdOf(principal.GetClaim(Claims.ClientId) ?? principal.GetPresenters().FirstOrDefault());
        if (tokenTenant != tenant.Id.ToString() || clientTenant != tenant.Id)
        {
            logger.LogWarning("Rejected a token issued for tenant {TokenTenant} on tenant {TenantId}", tokenTenant, tenant.Id);
            await Problem(context, StatusCodes.Status403Forbidden, "This token was issued for another tenant.");
            return;
        }

        if (!Guid.TryParse(principal.GetClaim(Claims.Subject), out var userId)
            || await access.GetCachedAsync(tenant.Id, userId, context.RequestAborted) is not { } membership)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
            await Problem(context, StatusCodes.Status401Unauthorized, "Access to this tenant has been revoked.");
            return;
        }

        currentUser.Set(userId, membership.Role);
        using (logger.BeginScope(new Dictionary<string, object> { ["UserId"] = userId }))
        {
            await next(context);
        }
    }

    private static Task Problem(HttpContext context, int status, string detail) =>
        TypedResults.Problem(statusCode: status, detail: detail).ExecuteAsync(context);
}

/// <summary>Authorization-server routes answer on the issuer host only; on tenant hosts they do not exist.</summary>
internal sealed class AuthHostGuardMiddleware(RequestDelegate next, IOptions<ShellIdentityOptions> options)
{
    private static readonly PathString[] AuthPaths = ["/connect", "/account", "/invitations", "/.well-known", "/signin-google", "/signin-microsoft"];

    public Task InvokeAsync(HttpContext context)
    {
        if (!string.Equals(context.Request.Host.Host, options.Value.AuthHost, StringComparison.OrdinalIgnoreCase)
            && AuthPaths.Any(path => context.Request.Path.StartsWithSegments(path)))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        return next(context);
    }
}
