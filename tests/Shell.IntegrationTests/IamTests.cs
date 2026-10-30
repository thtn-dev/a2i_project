using System.Net;
using System.Net.Http.Json;
using Shell.Core.Tenancy;
using Shell.Identity.Data;
using Shell.Identity.Endpoints;

namespace Shell.IntegrationTests;

/// <summary>
/// Tenant administration (<c>/iam</c>): who may use it, revocation taking effect on grants and tokens, owner rules, invitations
/// for new and existing accounts. Alice is the only owner of tenant A (Pool) and a member of tenant B (Silo, auth optional).
/// </summary>
[Collection(PostgresCollection.Name)]
public class IamTests(PostgresFixture db)
{
    private const string TenantA = DemoTenantSeeder.PoolTenantDomain;
    private const string TenantB = DemoTenantSeeder.SiloTenantDomain;
    private const string DemoPassword = DemoUserSeeder.Password;
    private const string Password = "Test-Passw0rd-1";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Only_owners_and_admins_of_the_tenant_with_a_token_of_that_tenant_can_administer_it()
    {
        await using var host = db.CreateHost();
        var tenantA = await db.TenantIdAsync(TenantA);
        var tenantB = await db.TenantIdAsync(TenantB);
        var aliceForA = (await new OidcTestClient(host).GetTokensAsync(DemoUserSeeder.Alice, DemoPassword, tenantA, TenantA)).AccessToken;
        var aliceForB = (await new OidcTestClient(host).GetTokensAsync(DemoUserSeeder.Alice, DemoPassword, tenantB, TenantB)).AccessToken;
        var bobForA = (await new OidcTestClient(host).GetTokensAsync(DemoUserSeeder.Bob, DemoPassword, tenantA, TenantA)).AccessToken;

        var members = await OidcTestClient.CallApiAsync(host, TenantA, HttpMethod.Get, "/iam/members", aliceForA);
        Assert.Equal(HttpStatusCode.OK, members.StatusCode);
        Assert.Contains((await members.Content.ReadFromJsonAsync<List<MemberDto>>(Ct))!, m => m.Email == DemoUserSeeder.Bob && m.Role == "member");

        Assert.Equal(HttpStatusCode.Forbidden, (await OidcTestClient.CallApiAsync(host, TenantA, HttpMethod.Get, "/iam/members", bobForA)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await OidcTestClient.CallApiAsync(host, TenantB, HttpMethod.Get, "/iam/members", aliceForB)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await OidcTestClient.CallApiAsync(host, TenantB, HttpMethod.Get, "/iam/members", aliceForA)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await OidcTestClient.CallApiAsync(host, TenantA, HttpMethod.Get, "/iam/members", null)).StatusCode);
    }

    [Fact]
    public async Task Revoking_a_member_revokes_its_refresh_tokens_and_access_at_once()
    {
        await using var host = db.CreateHost();
        var tenantA = await db.TenantIdAsync(TenantA);
        var email = NewEmail("revoked");
        var member = await CreateMemberAsync(host, email, tenantA, "member");
        var tokens = await member.GetTokensAsync(email, Password, tenantA, TenantA);
        var admin = await AdminTokenAsync(host, tenantA);
        var userId = await UserIdAsync(email);

        Assert.Equal(HttpStatusCode.NoContent,
            (await OidcTestClient.CallApiAsync(host, TenantA, HttpMethod.Post, $"/iam/members/{userId}/revoke", admin)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await OidcTestClient.CallApiAsync(host, TenantA, HttpMethod.Get, "/api/products", tokens.AccessToken)).StatusCode);
        Assert.Equal("invalid_grant", (await member.RefreshAsync(tenantA, tokens.RefreshToken!)).Error);
        Assert.Equal(0, await db.CountAsync(db.CatalogConnectionString,
            $"SELECT count(*) FROM identity.openiddict_tokens WHERE subject = '{userId}' AND status = 'valid' AND type = 'refresh_token'"));
        Assert.Contains(await AuditTypesAsync(tenantA, userId), type => type == "membership.revoked");

        // Restored members sign in again normally.
        Assert.Equal(HttpStatusCode.NoContent,
            (await OidcTestClient.CallApiAsync(host, TenantA, HttpMethod.Post, $"/iam/members/{userId}/restore", admin)).StatusCode);
        var again = await member.GetTokensAsync(email, Password, tenantA, TenantA);
        Assert.Equal(HttpStatusCode.OK, (await OidcTestClient.CallApiAsync(host, TenantA, HttpMethod.Get, "/api/products", again.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Tenant_keeps_an_owner_and_only_owners_manage_owners()
    {
        await using var host = db.CreateHost();
        var tenantA = await db.TenantIdAsync(TenantA);
        var aliceId = await UserIdAsync(DemoUserSeeder.Alice);
        var owner = await AdminTokenAsync(host, tenantA);

        Assert.Equal(HttpStatusCode.Conflict, (await OidcTestClient.CallApiAsync(host, TenantA, HttpMethod.Put, $"/iam/members/{aliceId}/role", owner,
            new ChangeRoleRequest("member"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await OidcTestClient.CallApiAsync(host, TenantA, HttpMethod.Post, $"/iam/members/{aliceId}/revoke", owner)).StatusCode);

        var adminEmail = NewEmail("admin");
        var admin = await (await CreateMemberAsync(host, adminEmail, tenantA, "admin")).GetTokensAsync(adminEmail, Password, tenantA, TenantA);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await OidcTestClient.CallApiAsync(host, TenantA, HttpMethod.Post, $"/iam/members/{aliceId}/revoke", admin.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await OidcTestClient.CallApiAsync(host, TenantA, HttpMethod.Post, "/iam/invitations", admin.AccessToken,
            new InviteRequest(NewEmail("x"), "owner"))).StatusCode);
    }

    [Fact]
    public async Task New_person_accepts_an_invitation_after_creating_an_account()
    {
        await using var host = db.CreateHost();
        var tenantA = await db.TenantIdAsync(TenantA);
        var email = NewEmail("invited");
        await InviteAsync(host, tenantA, email, "member");

        var browser = new OidcTestClient(host);
        var link = db.Emails.LastLinkTo(email);
        Assert.Contains("create an account", await browser.GetPageAsync(link));
        await browser.RegisterAsync(email, Password);
        Assert.Contains("id=\"accepted\"", await AcceptAsync(browser, link));

        var tokens = await browser.GetTokensAsync(email, Password, tenantA, TenantA);
        Assert.Equal(HttpStatusCode.OK, (await OidcTestClient.CallApiAsync(host, TenantA, HttpMethod.Get, "/api/products", tokens.AccessToken)).StatusCode);
        Assert.Contains(await AuditTypesAsync(tenantA, await UserIdAsync(email)), type => type == "invitation.accepted");

        // Single use.
        Assert.Contains("invalid, expired or already used", await AcceptAsync(browser, link));
    }

    [Fact]
    public async Task Existing_member_of_another_tenant_joins_with_the_same_account()
    {
        await using var host = db.CreateHost();
        var tenantA = await db.TenantIdAsync(TenantA);
        var tenantB = await db.TenantIdAsync(TenantB);
        var email = NewEmail("member-of-b");
        var browser = await CreateMemberAsync(host, email, tenantB, "member");

        await InviteAsync(host, tenantA, email, "admin");
        Assert.Contains("id=\"accepted\"", await AcceptAsync(browser, db.Emails.LastLinkTo(email)));

        var forA = await browser.GetTokensAsync(email, Password, tenantA, TenantA);
        var forB = await browser.GetTokensAsync(email, Password, tenantB, TenantB);
        Assert.Equal(HttpStatusCode.OK, (await OidcTestClient.CallApiAsync(host, TenantA, HttpMethod.Get, "/iam/members", forA.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await OidcTestClient.CallApiAsync(host, TenantB, HttpMethod.Get, "/api/products", forB.AccessToken)).StatusCode);
        Assert.Equal(1, await db.CountAsync(db.CatalogConnectionString, $"SELECT count(*) FROM identity.users WHERE email = '{email}'"));
    }

    [Fact]
    public async Task Invitation_cannot_be_accepted_by_another_account_nor_after_expiry()
    {
        await using var host = db.CreateHost();
        var tenantA = await db.TenantIdAsync(TenantA);
        var invited = NewEmail("invited");
        await InviteAsync(host, tenantA, invited, "member");
        var link = db.Emails.LastLinkTo(invited);

        var someoneElse = new OidcTestClient(host);
        await someoneElse.RegisterAsync(NewEmail("other"), Password);
        Assert.Contains("sent to another email address", await AcceptAsync(someoneElse, link));

        await db.ExecuteAsync($"UPDATE identity.tenant_invitations SET expires_at = now() - interval '1 minute' WHERE normalized_email = '{invited.ToUpperInvariant()}'");
        var owner = new OidcTestClient(host);
        await owner.RegisterAsync(invited, Password);
        Assert.Contains("invalid, expired or already used", await AcceptAsync(owner, link));
        Assert.Equal(0, await db.CountAsync(db.CatalogConnectionString,
            $"SELECT count(*) FROM identity.tenant_memberships m JOIN identity.users u ON u.id = m.user_id WHERE u.email = '{invited}'"));
    }

    internal static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@test.local";

    /// <summary>Registers an account (signed in on the returned browser) and makes it a member of the tenant.</summary>
    private async Task<OidcTestClient> CreateMemberAsync(ShellHostFactory host, string email, Guid tenantId, string role)
    {
        var browser = new OidcTestClient(host);
        await browser.RegisterAsync(email, Password);
        await db.ExecuteAsync($"""
            INSERT INTO identity.tenant_memberships (tenant_id, user_id, role, status, created_at, updated_at)
            SELECT '{tenantId}', id, '{role}', 'active', now(), now() FROM identity.users WHERE email = '{email}'
            """);
        return browser;
    }

    private async Task<string?> AdminTokenAsync(ShellHostFactory host, Guid tenantA) =>
        (await new OidcTestClient(host).GetTokensAsync(DemoUserSeeder.Alice, DemoPassword, tenantA, TenantA)).AccessToken;

    private async Task InviteAsync(ShellHostFactory host, Guid tenantA, string email, string role)
    {
        var response = await OidcTestClient.CallApiAsync(host, TenantA, HttpMethod.Post, "/iam/invitations",
            await AdminTokenAsync(host, tenantA), new InviteRequest(email, role));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>Antiforgery token from the login page: the invitation page has no form once the invitation is unusable.</summary>
    private static async Task<string> AcceptAsync(OidcTestClient browser, string link) =>
        await (await browser.PostFormAsync(link, new(), tokenPage: "/account/login")).Content.ReadAsStringAsync(Ct);

    private async Task<Guid> UserIdAsync(string email) => Guid.Parse((await db.ScalarAsync($"SELECT id::text FROM identity.users WHERE email = '{email}'"))!);

    private async Task<List<string>> AuditTypesAsync(Guid tenantId, Guid userId) =>
        [.. (await db.ScalarAsync($"SELECT string_agg(type, ',') FROM identity.audit_events WHERE tenant_id = '{tenantId}' AND user_id = '{userId}'"))!
            .Split(',')];
}
