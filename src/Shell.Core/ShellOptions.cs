using System.ComponentModel.DataAnnotations;
using Npgsql;

namespace Shell.Core;

/// <summary>Section <c>Shell</c>.</summary>
public sealed class ShellOptions
{
    public const string SectionName = "Shell";

    /// <summary>Time given to in-flight requests to complete on shutdown (SIGTERM).</summary>
    [Range(1, 300)]
    public int ShutdownTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Trust <c>X-Forwarded-For/Proto</c> from any proxy (the app is only reachable through the reverse proxy, e.g. Nginx terminating TLS).
    /// Needed for the authorization server to see HTTPS requests as HTTPS.
    /// </summary>
    public bool TrustForwardedHeaders { get; set; }
}

/// <summary>Section <c>ConnectionStrings</c>.</summary>
public sealed class ConnectionStringsOptions
{
    public const string SectionName = "ConnectionStrings";

    /// <summary>Master database: tenants and their domains. Created by the migrate command if missing.</summary>
    [Required(ErrorMessage = "ConnectionStrings:Catalog is required (env: ConnectionStrings__Catalog).")]
    public string Catalog { get; set; } = "";

    /// <summary>Shared database of all Pool tenants (one schema per module). Created by the migrate command.</summary>
    [Required(ErrorMessage = "ConnectionStrings:DefaultShared is required (env: ConnectionStrings__DefaultShared).")]
    public string DefaultShared { get; set; } = "";

    internal static bool IsValid(string value)
    {
        try
        {
            _ = new NpgsqlConnectionStringBuilder(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>Section <c>Tenancy</c>.</summary>
public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    /// <summary>
    /// Lets the <c>X-Tenant-Host</c> header override the request host (local testing, Swagger).
    /// Harmless for data access (tokens are bound to their tenant) but lets anyone pick the tenant: keep false in production.
    /// </summary>
    public bool AllowHostHeaderOverride { get; set; }

    /// <summary>How long a resolved tenant is cached per instance.</summary>
    [Range(1, 1440)]
    public int CacheMinutes { get; set; } = 10;

    /// <summary>How long an unknown domain is cached per instance (protects the catalog DB from spam).</summary>
    [Range(1, 3600)]
    public int NotFoundCacheSeconds { get; set; } = 60;

    /// <summary>Hosts serving platform pages that belong to no tenant (e.g. the authorization server). Requests to them skip tenant resolution.</summary>
    public List<string> PlatformHosts { get; set; } = [];

    /// <summary>The migrate command creates two demo tenants (Pool and Silo) when the catalog is empty.</summary>
    public bool SeedDemoTenants { get; set; }
}
