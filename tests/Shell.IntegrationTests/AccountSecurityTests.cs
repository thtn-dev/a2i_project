using System.Net;
using System.Text.RegularExpressions;
using Shell.Core.Tenancy;
using Shell.Identity;

namespace Shell.IntegrationTests;

/// <summary>Sign-in protections and account lifecycle on the authorization server.</summary>
[Collection(PostgresCollection.Name)]
public partial class AccountSecurityTests(PostgresFixture db)
{
    private const string TenantA = DemoTenantSeeder.PoolTenantDomain;
    private const string Password = "Test-Passw0rd-1";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Enabled_mfa_is_required_at_sign_in_and_recovery_codes_work_once()
    {
        await using var host = db.CreateHost();
        var email = IamTests.NewEmail("mfa");
        var owner = new OidcTestClient(host);
        await owner.RegisterAsync(email, Password);

        var key = AuthenticatorKey().Match(await owner.GetPageAsync("/account/mfa")).Groups[1].Value;
        Assert.NotEmpty(key);
        var enabled = await (await owner.PostFormAsync("/account/mfa?handler=Enable", new() { ["Code"] = Totp.Code(key) }, tokenPage: "/account/mfa"))
            .Content.ReadAsStringAsync(Ct);
        var recoveryCodes = RecoveryCode().Matches(enabled).Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(10, recoveryCodes.Count);

        // Password alone is not enough any more.
        var browser = new OidcTestClient(host);
        var login = await browser.SubmitLoginAsync(email, Password);
        Assert.StartsWith("/account/login-2fa", login.Headers.Location!.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Invalid code", await (await browser.PostFormAsync("/account/login-2fa", new() { ["Code"] = "000000" })).Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.Redirect, (await browser.PostFormAsync("/account/login-2fa", new() { ["Code"] = Totp.Code(key) })).StatusCode);
        Assert.Contains("Signed in as", await browser.GetPageAsync("/"));

        var withRecovery = new OidcTestClient(host);
        await withRecovery.SubmitLoginAsync(email, Password);
        Assert.Equal(HttpStatusCode.Redirect, (await withRecovery.PostFormAsync("/account/login-recovery", new() { ["Code"] = recoveryCodes[0] })).StatusCode);

        var reused = new OidcTestClient(host);
        await reused.SubmitLoginAsync(email, Password);
        Assert.Equal(HttpStatusCode.OK, (await reused.PostFormAsync("/account/login-recovery", new() { ["Code"] = recoveryCodes[0] })).StatusCode);
    }

    [Fact]
    public async Task Account_is_locked_after_five_wrong_passwords_and_attempts_are_audited()
    {
        await using var host = db.CreateHost();
        var email = IamTests.NewEmail("brute");
        await new OidcTestClient(host).RegisterAsync(email, Password);

        var attacker = new OidcTestClient(host);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await attacker.SubmitLoginAsync(email, $"wrong-{i}")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await attacker.SubmitLoginAsync(email, Password)).StatusCode); // Right password, still locked.
        var userId = await db.ScalarAsync($"SELECT id::text FROM identity.users WHERE email = '{email}'");
        // Attempts 1-4 fail, the 5th locks the account, the 6th (right password) hits the lock.
        Assert.Equal(4, await db.CountAsync(db.CatalogConnectionString, $"SELECT count(*) FROM identity.audit_events WHERE user_id = '{userId}' AND type = 'login.failed'"));
        Assert.Equal(2, await db.CountAsync(db.CatalogConnectionString, $"SELECT count(*) FROM identity.audit_events WHERE user_id = '{userId}' AND type = 'login.locked_out'"));
    }

    [Fact]
    public async Task Sign_in_endpoints_are_rate_limited_per_client()
    {
        await using var host = db.CreateHost(new Dictionary<string, string?> { ["Identity:RateLimitPerMinute"] = "3" });
        var browser = new OidcTestClient(host);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await browser.GetAsync("/account/login")).StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        // Tenant APIs are not under this limit.
        Assert.NotEqual(HttpStatusCode.TooManyRequests,
            (await OidcTestClient.CallApiAsync(host, DemoTenantSeeder.SiloTenantDomain, HttpMethod.Get, "/api/products", null)).StatusCode);
    }

    [Fact]
    public async Task Disabled_account_loses_every_token_and_cannot_sign_in_until_enabled()
    {
        await using var host = db.CreateHost();
        var tenantA = await db.TenantIdAsync(TenantA);
        var email = IamTests.NewEmail("disabled");
        var browser = new OidcTestClient(host);
        await browser.RegisterAsync(email, Password);
        await db.ExecuteAsync($"""
            INSERT INTO identity.tenant_memberships (tenant_id, user_id, role, status, created_at, updated_at)
            SELECT '{tenantA}', id, 'member', 'active', now(), now() FROM identity.users WHERE email = '{email}'
            """);
        var tokens = await browser.GetTokensAsync(email, Password, tenantA, TenantA);

        Assert.Equal(0, await IdentityCommands.RunAsync(host.Services, "disable-user", email, Ct));

        Assert.Equal(HttpStatusCode.Unauthorized, (await OidcTestClient.CallApiAsync(host, TenantA, HttpMethod.Get, "/api/products", tokens.AccessToken)).StatusCode);
        Assert.Equal("invalid_grant", (await browser.RefreshAsync(tenantA, tokens.RefreshToken!)).Error);
        Assert.Equal(HttpStatusCode.OK, (await new OidcTestClient(host).SubmitLoginAsync(email, Password)).StatusCode);

        Assert.Equal(0, await IdentityCommands.RunAsync(host.Services, "enable-user", email, Ct));
        Assert.Equal(HttpStatusCode.Redirect, (await new OidcTestClient(host).SubmitLoginAsync(email, Password)).StatusCode);
    }

    [Fact]
    public async Task Social_login_creates_a_personal_account_but_never_takes_over_an_existing_one()
    {
        await using var host = db.CreateHost();

        var newcomer = new OidcTestClient(host);
        var newEmail = IamTests.NewEmail("social");
        await SocialLoginAsync(newcomer, newEmail, subject: Guid.NewGuid().ToString());
        Assert.Contains("Signed in as", await newcomer.GetPageAsync("/"));
        Assert.Equal(1, await LinkedLoginsAsync(newEmail));

        // Someone at the provider claims the email of an existing account: refused, nothing linked.
        var existingEmail = IamTests.NewEmail("existing");
        var owner = new OidcTestClient(host);
        await owner.RegisterAsync(existingEmail, Password);
        var intruder = new OidcTestClient(host);
        var refused = await SocialLoginAsync(intruder, existingEmail, subject: Guid.NewGuid().ToString());
        Assert.Contains("already exists", refused);
        Assert.DoesNotContain("Signed in as", await intruder.GetPageAsync("/"));
        Assert.Equal(0, await LinkedLoginsAsync(existingEmail));

        // The owner links it explicitly while signed in.
        var link = await owner.PostFormAsync("/account/logins?handler=Link", new() { ["provider"] = FakeSocialHandler.SchemeName }, tokenPage: "/account/logins",
            headers: new Dictionary<string, string> { [FakeSocialHandler.EmailHeader] = existingEmail, [FakeSocialHandler.SubjectHeader] = "owner-subject" });
        await owner.GetAsync(link.Headers.Location!.PathAndQuery());
        Assert.Equal(1, await LinkedLoginsAsync(existingEmail));
    }

    [Fact]
    public async Task Forgotten_password_is_reset_through_the_emailed_link_only()
    {
        await using var host = db.CreateHost();
        var email = IamTests.NewEmail("forgot");
        await new OidcTestClient(host).RegisterAsync(email, Password);

        var browser = new OidcTestClient(host);
        await browser.PostFormAsync("/account/forgot-password", new() { ["Email"] = email });
        var link = db.Emails.LastLinkTo(email);

        var forged = Regex.Replace(link, "token=[^&]+", "token=forged");
        Assert.Contains("invalid or expired", await (await browser.PostFormAsync(forged, new() { ["Password"] = "New-Passw0rd-2" })).Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.Redirect, (await browser.PostFormAsync(link, new() { ["Password"] = "New-Passw0rd-2" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await new OidcTestClient(host).SubmitLoginAsync(email, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await new OidcTestClient(host).SubmitLoginAsync(email, "New-Passw0rd-2")).StatusCode);
    }

    /// <summary>Clicks "Continue with Fake" and follows the provider round trip; returns the final page.</summary>
    private static async Task<string> SocialLoginAsync(OidcTestClient browser, string email, string subject)
    {
        var challenge = await browser.PostFormAsync("/account/external-login", new() { ["provider"] = FakeSocialHandler.SchemeName }, tokenPage: "/account/login",
            headers: new Dictionary<string, string> { [FakeSocialHandler.EmailHeader] = email, [FakeSocialHandler.SubjectHeader] = subject });
        var callback = await browser.GetAsync(challenge.Headers.Location!.PathAndQuery());
        return await callback.Content.ReadAsStringAsync(Ct);
    }

    private Task<long> LinkedLoginsAsync(string email) => db.CountAsync(db.CatalogConnectionString,
        $"SELECT count(*) FROM identity.user_logins l JOIN identity.users u ON u.id = l.user_id WHERE u.email = '{email}'");

    [GeneratedRegex("id=\"authenticator-key\">([A-Z2-7]+)<")]
    private static partial Regex AuthenticatorKey();

    [GeneratedRegex("<li>([a-zA-Z0-9-]+)</li>")]
    private static partial Regex RecoveryCode();
}

internal static class UriExtensions
{
    /// <summary>Path and query of an absolute or relative redirect.</summary>
    public static string PathAndQuery(this Uri uri) => uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString;
}
