using System.ComponentModel.DataAnnotations;

namespace Shell.Identity;

/// <summary>Section <c>Identity</c>.</summary>
public sealed class ShellIdentityOptions
{
    public const string SectionName = "Identity";

    /// <summary>
    /// Public URL of the authorization server (e.g. <c>https://auth.app.com/</c>). Its host serves the login pages and the
    /// OpenID Connect endpoints and belongs to no tenant.
    /// </summary>
    [Required(ErrorMessage = "Identity:Issuer is required (env: Identity__Issuer).")]
    public Uri? Issuer { get; set; }

    /// <summary>False only for local/demo setups over plain HTTP: also drops the <c>__Host-</c> cookie prefix and the Secure flag.</summary>
    public bool RequireHttps { get; set; } = true;

    [Range(1, 60)]
    public int AccessTokenMinutes { get; set; } = 10;

    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>How long an API instance trusts a membership check. Upper bound of the delay before a revoked member loses access.</summary>
    [Range(1, 300)]
    public int MembershipCacheSeconds { get; set; } = 30;

    /// <summary>Requests per minute and client IP on sign-in, registration, password, invitation and token endpoints (per instance).</summary>
    [Range(1, 100_000)]
    public int RateLimitPerMinute { get; set; } = 60;

    public ClientOptions Clients { get; set; } = new();

    /// <summary>Social login providers for personal accounts. A provider is enabled when its client id is set.</summary>
    public ExternalProviderOptions Google { get; set; } = new();

    public ExternalProviderOptions Microsoft { get; set; } = new();

    public KeyOptions Keys { get; set; } = new();

    /// <summary>Host name (lowercase) of <see cref="Issuer"/>.</summary>
    public string AuthHost => Issuer!.Host.ToLowerInvariant();

    /// <summary>OAuth client registered for every tenant; <c>{domain}</c> is replaced by each of the tenant's domains.</summary>
    public sealed class ClientOptions
    {
        [MinLength(1)]
        public List<string> RedirectUris { get; set; } = ["https://{domain}/signin-callback"];

        public List<string> PostLogoutRedirectUris { get; set; } = ["https://{domain}/"];
    }

    public sealed class ExternalProviderOptions
    {
        public string? ClientId { get; set; }

        /// <summary>Secret: environment variable or secret store, never appsettings.</summary>
        public string? ClientSecret { get; set; }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
    }

    /// <summary>
    /// Token signing and encryption certificates: every <c>signing-*.pfx</c> and <c>encryption-*.pfx</c> in <see cref="Path"/>.
    /// All are used to validate; the one valid the longest signs/encrypts, so rotation = add a new file, remove the old one later.
    /// </summary>
    public sealed class KeyOptions
    {
        /// <summary>Directory of the certificates; relative paths start at the content root. Shared by every instance.</summary>
        [Required]
        public string Path { get; set; } = "keys";

        /// <summary>Password of the PFX files (secret).</summary>
        public string? Password { get; set; }

        /// <summary>Creates self-signed certificates when none exist. Development and demo only: production mounts real ones.</summary>
        public bool AutoGenerate { get; set; }
    }
}
