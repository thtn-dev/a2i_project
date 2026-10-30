using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using Shell.Abstractions;
using Shell.Core;
using Shell.Core.Data;
using Shell.Identity.Access;
using Shell.Identity.Audit;
using Shell.Identity.Clients;
using Shell.Identity.Data;
using Shell.Identity.Email;
using Shell.Identity.Endpoints;
using Shell.Identity.Keys;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Shell.Identity;

public static class ShellIdentityExtensions
{
    /// <summary>Picks the cookie session on the authorization server host and bearer tokens on tenant hosts.</summary>
    private const string HostScheme = "shell";

    /// <summary>Rate limiter policy of the endpoints exposed to credential guessing.</summary>
    private const string AuthRateLimit = "auth";

    private static readonly string[] IdentityCookieSchemes =
    [
        IdentityConstants.ApplicationScheme, IdentityConstants.ExternalScheme,
        IdentityConstants.TwoFactorUserIdScheme, IdentityConstants.TwoFactorRememberMeScheme,
    ];

    /// <summary>
    /// Authorization server (ASP.NET Core Identity + OpenIddict) on the <see cref="ShellIdentityOptions.Issuer"/> host,
    /// and bearer-token validation bound to the tenant on every other host. Call after <c>AddShell</c>.
    /// </summary>
    public static IServiceCollection AddShellIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ShellIdentityOptions>()
            .BindConfiguration(ShellIdentityOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(o => o.Issuer is { IsAbsoluteUri: true }, "Identity:Issuer must be an absolute URL.")
            .Validate(o => o.Clients.RedirectUris.Concat(o.Clients.PostLogoutRedirectUris).All(uri => uri.Contains("{domain}", StringComparison.Ordinal)),
                "Identity:Clients redirect URIs must contain {domain}.")
            .ValidateOnStart();

        // The authorization server host belongs to no tenant.
        services.AddOptions<TenancyOptions>()
            .Configure<IOptions<ShellIdentityOptions>>((tenancy, identity) => tenancy.PlatformHosts.Add(identity.Value.AuthHost));

        services.AddDbContext<AuthDbContext>((sp, options) => options.UseNpgsql(
            sp.GetRequiredService<IOptions<ConnectionStringsOptions>>().Value.Catalog,
            npgsql => npgsql.MigrationsHistoryTable(ModuleDbContextExtensions.MigrationsHistoryTable, AuthDbContext.Schema)));

        // Shared by every instance: cookies and Identity tokens protected by one instance are readable by all.
        services.AddDataProtection().SetApplicationName("shell").PersistKeysToDbContext<AuthDbContext>();

        var authentication = services.AddAuthentication(HostScheme)
            .AddPolicyScheme(HostScheme, displayName: null, options => options.ForwardDefaultSelector = context =>
                IsAuthHost(context) ? IdentityConstants.ApplicationScheme : OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        authentication.AddIdentityCookies();

        // Social login: registered at start-up only when configured (schemes are fixed once the host is built).
        var settings = configuration.GetSection(ShellIdentityOptions.SectionName).Get<ShellIdentityOptions>() ?? new();
        if (settings.Google.IsConfigured)
        {
            authentication.AddGoogle(options =>
            {
                options.SignInScheme = IdentityConstants.ExternalScheme;
                options.ClientId = settings.Google.ClientId!;
                options.ClientSecret = settings.Google.ClientSecret!;
                options.ClaimActions.MapJsonKey("email_verified", "verified_email");
            });
        }

        if (settings.Microsoft.IsConfigured)
        {
            authentication.AddMicrosoftAccount(options =>
            {
                options.SignInScheme = IdentityConstants.ExternalScheme;
                options.ClientId = settings.Microsoft.ClientId!;
                options.ClientSecret = settings.Microsoft.ClientSecret!;
            });
        }

        services.AddIdentityCore<User>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddSignInManager()
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddDefaultTokenProviders();

        // Cookies of the authorization server: host-only, so never sent to tenant (sub)domains.
        foreach (var scheme in IdentityCookieSchemes)
        {
            services.AddOptions<CookieAuthenticationOptions>(scheme)
                .Configure<IOptions<ShellIdentityOptions>>((cookie, identity) =>
                {
                    var https = identity.Value.RequireHttps;
                    var name = scheme == IdentityConstants.ApplicationScheme
                        ? "session"
                        : scheme.Replace("Identity.", "", StringComparison.Ordinal).ToLowerInvariant();
                    cookie.Cookie.Name = https ? $"__Host-shell.{name}" : $"shell.{name}";
                    cookie.Cookie.SecurePolicy = https ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
                    cookie.Cookie.HttpOnly = true;
                    cookie.Cookie.SameSite = SameSiteMode.Lax;
                    cookie.Cookie.Path = "/";
                    if (scheme == IdentityConstants.ApplicationScheme)
                    {
                        cookie.LoginPath = "/account/login";
                        cookie.LogoutPath = "/account/logout";
                        cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
                        cookie.SlidingExpiration = true;
                    }
                });
        }

        // Disabled accounts, password resets, MFA changes: the account's other sessions end within a minute.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(1));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IOptions<ShellIdentityOptions>>().Value.RateLimitPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                }));
        });
        services.AddAntiforgery();
        services.AddOptions<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>()
            .Configure<IOptions<ShellIdentityOptions>>((antiforgery, identity) =>
            {
                antiforgery.Cookie.Name = identity.Value.RequireHttps ? "__Host-shell.antiforgery" : "shell.antiforgery";
                antiforgery.Cookie.SecurePolicy = identity.Value.RequireHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            });

        services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<AuthDbContext>().ReplaceDefaultEntities<Guid>())
            .AddServer(options =>
            {
                options.SetAuthorizationEndpointUris("connect/authorize")
                    .SetTokenEndpointUris("connect/token")
                    .SetEndSessionEndpointUris("connect/endsession")
                    .SetUserInfoEndpointUris("connect/userinfo");
                options.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow().RequireProofKeyForCodeExchange();
                options.RegisterScopes(Scopes.Profile, Scopes.Email, Scopes.OfflineAccess, TenantClients.ApiScope);
                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
                options.AddAudiences(TenantClients.ApiAudience);
            });

        services.AddSingleton<IdentityKeyRing>();
        services.AddOptions<OpenIddictServerOptions>()
            .Configure<IdentityKeyRing, IOptions<ShellIdentityOptions>>((server, keys, identity) =>
            {
                server.Issuer = identity.Value.Issuer;
                server.AccessTokenLifetime = TimeSpan.FromMinutes(identity.Value.AccessTokenMinutes);
                server.RefreshTokenLifetime = TimeSpan.FromDays(identity.Value.RefreshTokenDays);
                foreach (var certificate in keys.Signing)
                {
                    server.SigningCredentials.Add(new SigningCredentials(new X509SecurityKey(certificate), SecurityAlgorithms.RsaSha256));
                }

                foreach (var certificate in keys.Encryption)
                {
                    server.EncryptionCredentials.Add(new EncryptingCredentials(
                        new X509SecurityKey(certificate), SecurityAlgorithms.RsaOAEP, SecurityAlgorithms.Aes256CbcHmacSha512));
                }
            })
            .ValidateOnStart(); // Missing certificates stop the host at start-up, not at the first sign-in.
        services.AddOptions<OpenIddictServerAspNetCoreOptions>()
            .Configure<IOptions<ShellIdentityOptions>>((aspNetCore, identity) =>
                aspNetCore.DisableTransportSecurityRequirement = !identity.Value.RequireHttps);

        services.AddRazorPages();
        services.AddHttpContextAccessor();
        services.AddScoped<IamContext>();
        services.AddScoped<TenantAccessService>();
        services.AddScoped<GrantRevoker>();
        services.AddScoped<MembershipService>();
        services.AddScoped<UserAccountService>();
        services.AddScoped<AuditLog>();
        services.AddSingleton<AccountLinks>();
        services.TryAddSingleton<IAccountEmailSender, LoggingAccountEmailSender>();
        services.AddScoped<TenantClientSynchronizer>();
        services.AddScoped<DemoUserSeeder>();
        services.AddTransient<IMigrationStep, IdentityMigrationStep>();
        return services;
    }

    /// <summary>
    /// Rate limiting, authentication, token ↔ tenant binding, authorization. Place right after <c>UseTenantResolution</c>.
    /// The rate limiter comes first because OpenIddict already validates token requests during authentication.
    /// </summary>
    public static IApplicationBuilder UseShellIdentity(this IApplicationBuilder app)
    {
        app.UseRateLimiter();
        app.UseMiddleware<AuthHostGuardMiddleware>();
        app.UseAuthentication();
        app.UseMiddleware<TenantAccessMiddleware>();
        app.UseAuthorization();
        return app;
    }

    /// <summary>
    /// Login pages and OpenID Connect endpoints on the authorization server host (rate limited),
    /// and tenant administration (<c>/iam</c>) on tenant hosts.
    /// </summary>
    public static IEndpointRouteBuilder MapShellIdentity(this IEndpointRouteBuilder endpoints)
    {
        var authHost = endpoints.ServiceProvider.GetRequiredService<IOptions<ShellIdentityOptions>>().Value.AuthHost;
        AuthorizationEndpoints.Map(endpoints.MapGroup("").RequireHost(authHost).RequireRateLimiting(AuthRateLimit).ExcludeFromDescription());
        endpoints.MapRazorPages().RequireHost(authHost).RequireRateLimiting(AuthRateLimit);
        IamEndpoints.Map(endpoints);
        return endpoints;
    }

    private static bool IsAuthHost(HttpContext context) => string.Equals(
        context.Request.Host.Host,
        context.RequestServices.GetRequiredService<IOptions<ShellIdentityOptions>>().Value.AuthHost,
        StringComparison.OrdinalIgnoreCase);
}
