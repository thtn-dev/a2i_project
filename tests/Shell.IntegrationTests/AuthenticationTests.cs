using System.Net;
using System.Net.Http.Json;
using Modules.Products.Contracts;
using Shell.Core.Tenancy;
using Shell.Identity.Data;

namespace Shell.IntegrationTests;

/// <summary>
/// Authorization server + tenant APIs end to end: every tenant API needs a token, tokens are bound to one tenant,
/// only active members get them, and moving a tenant between Pool and Silo does not affect authentication.
/// Demo data: tenant A (Pool), tenant B (Silo); alice is owner of A and member of B, bob is member of A.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AuthenticationTests(PostgresFixture db)
{
    private const string TenantA = DemoTenantSeeder.PoolTenantDomain;
    private const string TenantB = DemoTenantSeeder.SiloTenantDomain;
    private const string Password = DemoUserSeeder.Password;

    [Fact]
    public async Task Member_gets_a_token_for_its_tenant_and_business_data_references_its_stable_user_id()
    {
        await using var host = db.CreateHost();
        var tenantA = await db.TenantIdAsync(TenantA);
        var tokens = await new OidcTestClient(host).GetTokensAsync(DemoUserSeeder.Alice, Password, tenantA, TenantA);
        Assert.NotNull(tokens.RefreshToken);

        var created = await OidcTestClient.CallApiAsync(host, TenantA, HttpMethod.Post, "/api/products", tokens.AccessToken,
            new CreateProductRequest($"alice-{Guid.NewGuid()}", 5m));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var product = (await created.Content.ReadFromJsonAsync<ProductDto>(TestContext.Current.CancellationToken))!;
        var aliceId = await db.CountAsync(db.CatalogConnectionString, $"SELECT 1 FROM identity.users WHERE id = '{product.CreatedBy}' AND email = '{DemoUserSeeder.Alice}'");
        Assert.Equal(1, aliceId);
        Assert.Equal(HttpStatusCode.OK, (await OidcTestClient.CallApiAsync(host, TenantA, HttpMethod.Get, "/api/products", tokens.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Token_of_one_tenant_is_rejected_by_another_tenant()
    {
        await using var host = db.CreateHost();
        var tenantA = await db.TenantIdAsync(TenantA);
        var tokenForA = (await new OidcTestClient(host).GetTokensAsync(DemoUserSeeder.Alice, Password, tenantA, TenantA)).AccessToken;

        // Alice is also a member of B, but this token was issued for A: B must refuse it, by real host and by override header.
        Assert.Equal(HttpStatusCode.Forbidden, (await OidcTestClient.CallApiAsync(host, TenantB, HttpMethod.Get, "/api/products", tokenForA)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await OidcTestClient.CallApiAsync(host, TenantB, HttpMethod.Get, "/api/products", tokenForA, viaHeader: true)).StatusCode);
    }

    [Fact]
    public async Task Switching_tenant_reuses_the_session_and_gives_a_token_for_the_other_tenant()
    {
        await using var host = db.CreateHost();
        var client = new OidcTestClient(host);
        var tenantA = await db.TenantIdAsync(TenantA);
        var tenantB = await db.TenantIdAsync(TenantB);

        await client.GetTokensAsync(DemoUserSeeder.Alice, Password, tenantA, TenantA);
        var tokenForB = await client.GetTokensAsync(DemoUserSeeder.Alice, "not-needed: session cookie", tenantB, TenantB);

        Assert.Equal(HttpStatusCode.OK, (await OidcTestClient.CallApiAsync(host, TenantB, HttpMethod.Get, "/api/products", tokenForB.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await OidcTestClient.CallApiAsync(host, TenantA, HttpMethod.Get, "/api/products", tokenForB.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Non_member_gets_no_token()
    {
        await using var host = db.CreateHost();
        var tenantB = await db.TenantIdAsync(TenantB);

        var result = await new OidcTestClient(host).AuthorizeAsync(DemoUserSeeder.Bob, Password, tenantB, TenantB);

        Assert.Null(result.Code);
        Assert.Equal("access_denied", result.Error);
    }

    [Fact]
    public async Task Client_of_a_tenant_cannot_redirect_to_another_tenants_domain()
    {
        await using var host = db.CreateHost();
        var tenantA = await db.TenantIdAsync(TenantA);

        var result = await new OidcTestClient(host).AuthorizeAsync(
            DemoUserSeeder.Alice, Password, tenantA, TenantA, redirectUri: OidcTestClient.RedirectUri(TenantB));

        Assert.Null(result.Code);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode); // Never redirected to the foreign domain.
    }

    [Fact]
    public async Task Tenant_apis_refuse_requests_without_a_valid_token()
    {
        await using var host = db.CreateHost();

        foreach (var tenant in new[] { TenantA, TenantB })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await OidcTestClient.CallApiAsync(host, tenant, HttpMethod.Get, "/api/products", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await OidcTestClient.CallApiAsync(host, tenant, HttpMethod.Get, "/api/products", "garbage")).StatusCode);
        }
    }

    [Fact]
    public async Task Revoked_member_cannot_refresh_nor_use_its_access_token()
    {
        await using var host = db.CreateHost();
        var tenantA = await db.TenantIdAsync(TenantA);
        var client = new OidcTestClient(host);
        var tokens = await client.GetTokensAsync(DemoUserSeeder.Bob, Password, tenantA, TenantA);
        Assert.Equal(HttpStatusCode.OK, (await client.RefreshAsync(tenantA, tokens.RefreshToken!)).StatusCode);

        await SetMembershipStatusAsync(DemoUserSeeder.Bob, tenantA, "revoked");
        try
        {
            Assert.Equal("invalid_grant", (await client.RefreshAsync(tenantA, tokens.RefreshToken!)).Error);

            await using var otherInstance = db.CreateHost(); // Fresh membership cache, like another instance after the cache expired.
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await OidcTestClient.CallApiAsync(otherInstance, TenantA, HttpMethod.Get, "/api/products", tokens.AccessToken)).StatusCode);
        }
        finally
        {
            await SetMembershipStatusAsync(DemoUserSeeder.Bob, tenantA, "active");
        }
    }

    [Fact]
    public async Task Moving_a_tenant_from_pool_to_silo_keeps_accounts_memberships_and_tokens()
    {
        var domain = $"mover-{Guid.NewGuid():N}.localhost";
        var tenantId = Guid.NewGuid();
        await db.ExecuteAsync($"INSERT INTO catalog.tenants (id, name, is_active, is_isolated_db) VALUES ('{tenantId}', 'Mover', true, false)");
        await db.ExecuteAsync($"INSERT INTO catalog.tenant_domains (id, tenant_id, domain) VALUES (gen_random_uuid(), '{tenantId}', '{domain}')");
        await db.ExecuteAsync($"""
            INSERT INTO identity.tenant_memberships (tenant_id, user_id, role, status, created_at, updated_at)
            SELECT '{tenantId}', id, 'member', 'active', now(), now() FROM identity.users WHERE email = '{DemoUserSeeder.Bob}'
            """);

        string accessToken;
        await using (var pool = db.CreateHost())
        {
            await PostgresFixture.MigrateAsync(pool); // Registers the new tenant's OAuth client.
            accessToken = (await new OidcTestClient(pool).GetTokensAsync(DemoUserSeeder.Bob, Password, tenantId, domain)).AccessToken!;
            Assert.Equal(HttpStatusCode.Created, (await OidcTestClient.CallApiAsync(pool, domain, HttpMethod.Post, "/api/products", accessToken,
                new CreateProductRequest("in-pool", 1m))).StatusCode);
        }

        var siloDatabase = $"shell_mover_{Guid.NewGuid():N}";
        await db.ExecuteAsync($"UPDATE catalog.tenants SET is_isolated_db = true, connection_string = '{db.ForDatabase(siloDatabase)}' WHERE id = '{tenantId}'");

        await using var silo = db.CreateHost(); // New instance: no cached tenant.
        await PostgresFixture.MigrateAsync(silo); // Creates the tenant's own DB.
        var created = await OidcTestClient.CallApiAsync(silo, domain, HttpMethod.Post, "/api/products", accessToken, new CreateProductRequest("in-silo", 1m));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode); // Same token, same membership.
        Assert.Equal(1, await db.CountAsync(db.ForDatabase(siloDatabase), "SELECT count(*) FROM products.products WHERE name = 'in-silo'"));
    }

    [Fact]
    public async Task Authentication_data_lives_only_in_the_catalog_database()
    {
        const string identityTables = "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'identity'";

        Assert.True(await db.CountAsync(db.CatalogConnectionString, identityTables) > 0);
        Assert.Equal(0, await db.CountAsync(db.SharedConnectionString, identityTables));
        Assert.Equal(0, await db.CountAsync(db.TenantBConnectionString, identityTables));
    }

    [Fact]
    public async Task Authorization_server_routes_do_not_exist_on_tenant_hosts()
    {
        await using var host = db.CreateHost();
        var tenantHost = host.CreateClient(new() { BaseAddress = new Uri($"http://{TenantA}") });

        Assert.Equal(HttpStatusCode.NotFound, (await tenantHost.GetAsync("/account/login", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tenantHost.GetAsync("/.well-known/openid-configuration", TestContext.Current.CancellationToken)).StatusCode);
    }

    private Task SetMembershipStatusAsync(string email, Guid tenantId, string status) => db.ExecuteAsync($"""
        UPDATE identity.tenant_memberships SET status = '{status}'
        WHERE tenant_id = '{tenantId}' AND user_id = (SELECT id FROM identity.users WHERE email = '{email}')
        """);
}
