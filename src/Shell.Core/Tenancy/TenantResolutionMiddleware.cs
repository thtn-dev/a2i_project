using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Shell.Core.Tenancy;

/// <summary>
/// Resolves the tenant from the request host (or the <c>X-Tenant-Host</c> header when allowed):
/// unknown → 404, locked → 403, otherwise stores it in <see cref="TenantContext"/> for the rest of the request.
/// System routes (<c>/health</c>, <c>/swagger</c>) and platform hosts (<see cref="TenancyOptions.PlatformHosts"/>, e.g. the
/// authorization server) need no tenant.
/// </summary>
public sealed class TenantResolutionMiddleware(
    RequestDelegate next,
    IOptions<TenancyOptions> options,
    ILogger<TenantResolutionMiddleware> logger)
{
    public const string TenantHostHeader = "X-Tenant-Host";

    private static readonly PathString[] SystemPaths = ["/health", "/swagger"];

    public async Task InvokeAsync(HttpContext context, TenantStore store, TenantContext tenantContext)
    {
        if (SystemPaths.Any(path => context.Request.Path.StartsWithSegments(path)) || IsPlatformHost(context))
        {
            await next(context);
            return;
        }

        var domain = ResolveDomain(context);
        var tenant = domain is null ? null : await store.FindByDomainAsync(domain, context.RequestAborted);

        if (tenant is null)
        {
            await TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, detail: $"No tenant for host '{domain}'.")
                .ExecuteAsync(context);
            return;
        }

        if (!tenant.IsActive)
        {
            await TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, detail: "This tenant is locked.")
                .ExecuteAsync(context);
            return;
        }

        tenantContext.Set(tenant);
        using (logger.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenant.Id }))
        {
            await next(context);
        }
    }

    /// <summary>Uses the real host only: the override header must not turn a platform host into a tenant or the reverse.</summary>
    private bool IsPlatformHost(HttpContext context) =>
        Normalize(context.Request.Host.Host) is { } host
        && options.Value.PlatformHosts.Any(platform => string.Equals(platform, host, StringComparison.OrdinalIgnoreCase));

    private string? ResolveDomain(HttpContext context)
    {
        string? host = null;
        if (options.Value.AllowHostHeaderOverride)
        {
            host = context.Request.Headers[TenantHostHeader].FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            host = context.Request.Host.Host;
        }

        return Normalize(host);
    }

    /// <summary>Lowercase host without port or trailing dot; null when empty.</summary>
    public static string? Normalize(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var hostOnly = HostString.FromUriComponent(host.Trim()).Host;
        return hostOnly.TrimEnd('.').ToLowerInvariant() is { Length: > 0 } normalized ? normalized : null;
    }
}
