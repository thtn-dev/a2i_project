using System.Net;
using System.Net.Http.Json;
using Modules.Products.Contracts;
using Shell.Core.Tenancy;
using Shell.Identity.Data;

namespace Shell.IntegrationTests;

/// <summary>
/// End to end: migrate command, tenant resolution, Pool vs Silo storage. Calls use alice's tokens (member of A and B).
/// </summary>
[Collection(PostgresCollection.Name)]
public class TenancyTests(PostgresFixture db)
{
    private const string TenantA = DemoTenantSeeder.PoolTenantDomain;
    private const string TenantB = DemoTenantSeeder.SiloTenantDomain;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migrate_creates_catalog_shared_and_silo_databases_and_seeds_demo_tenants()
    {
        // Running again is a no-op (and does not seed twice).
        await using var host = db.CreateHost();
        await PostgresFixture.MigrateAsync(host);

        const string demoTenants = $"""
            SELECT count(*) FROM catalog.tenants t JOIN catalog.tenant_domains d ON d.tenant_id = t.id
            WHERE d.domain IN ('{TenantA}', '{TenantB}')
            """;
        Assert.Equal(2, await db.CountAsync(db.CatalogConnectionString, demoTenants));
        Assert.Equal(1, await db.CountAsync(db.CatalogConnectionString, demoTenants + " AND t.is_isolated_db"));
        Assert.Equal(2, await db.CountAsync(db.CatalogConnectionString, $"SELECT count(*) FROM identity.users WHERE email IN ('{DemoUserSeeder.Alice}', '{DemoUserSeeder.Bob}')"));

        const string productsTable =
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'products' AND table_name IN ('products', '__ef_migrations_history')";
        Assert.Equal(2, await db.CountAsync(db.SharedConnectionString, productsTable));
        Assert.Equal(2, await db.CountAsync(db.TenantBConnectionString, productsTable));
    }

    [Fact]
    public async Task Each_tenant_only_sees_its_own_products_and_silo_data_stays_in_its_own_database()
    {
        await using var host = db.CreateHost();
        var client = host.CreateClient();
        var alice = new OidcTestClient(host);
        var tokenA = await AliceTokenAsync(alice, TenantA);
        var tokenB = await AliceTokenAsync(alice, TenantB);
        var nameA = $"A-{Guid.NewGuid()}";
        var nameB = $"B-{Guid.NewGuid()}";

        var createdA = await CreateProductAsync(client, TenantA, nameA, tokenA);
        await CreateProductAsync(client, TenantB, nameB, tokenB);

        var seenByA = await ListProductsAsync(client, TenantA, tokenA);
        var seenByB = await ListProductsAsync(client, TenantB, tokenB);
        Assert.Contains(nameA, seenByA);
        Assert.DoesNotContain(nameB, seenByA);
        Assert.Contains(nameB, seenByB);
        Assert.DoesNotContain(nameA, seenByB);

        // A's product id is invisible to B.
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"/api/products/{createdA.Id}", TenantB, tokenB)).StatusCode);

        // Pool tenant A → shared DB; Silo tenant B → its own DB only.
        Assert.Equal(1, await db.CountAsync(db.SharedConnectionString, $"SELECT count(*) FROM products.products WHERE name = '{nameA}'"));
        Assert.Equal(0, await db.CountAsync(db.SharedConnectionString, $"SELECT count(*) FROM products.products WHERE name = '{nameB}'"));
        Assert.Equal(1, await db.CountAsync(db.TenantBConnectionString, $"SELECT count(*) FROM products.products WHERE name = '{nameB}'"));
    }

    [Fact]
    public async Task Tenant_is_resolved_from_the_request_host()
    {
        await using var host = db.CreateHost();
        var client = host.CreateClient(new() { BaseAddress = new Uri($"http://{TenantB}") });
        client.DefaultRequestHeaders.Authorization = new("Bearer", await AliceTokenAsync(new OidcTestClient(host), TenantB));
        var name = $"by-host-{Guid.NewGuid()}";

        var response = await client.PostAsJsonAsync("/api/products", new CreateProductRequest(name, 1m), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, await db.CountAsync(db.TenantBConnectionString, $"SELECT count(*) FROM products.products WHERE name = '{name}'"));
    }

    [Fact]
    public async Task Unknown_host_gets_404_and_locked_tenant_gets_403()
    {
        var lockedDomain = $"locked-{Guid.NewGuid():N}.localhost";
        await db.CountAsync(db.CatalogConnectionString, $"""
            WITH t AS (
                INSERT INTO catalog.tenants (id, name, is_active, is_isolated_db) VALUES (gen_random_uuid(), 'Locked', false, false) RETURNING id)
            INSERT INTO catalog.tenant_domains (id, tenant_id, domain) SELECT gen_random_uuid(), id, '{lockedDomain}' FROM t
            RETURNING 1
            """);

        await using var host = db.CreateHost();
        var client = host.CreateClient();

        var unknown = await SendAsync(client, HttpMethod.Get, "/api/products", "unknown.localhost");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, "/api/products", lockedDomain)).StatusCode);
    }

    [Fact]
    public async Task System_routes_need_no_tenant()
    {
        await using var host = db.CreateHost();
        var client = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/products", Ct)).StatusCode); // host "localhost" is no tenant
    }

    private async Task<string> AliceTokenAsync(OidcTestClient browser, string domain) =>
        (await browser.GetTokensAsync(DemoUserSeeder.Alice, DemoUserSeeder.Password, await db.TenantIdAsync(domain), domain)).AccessToken!;

    private static async Task<ProductDto> CreateProductAsync(HttpClient client, string tenant, string name, string? accessToken = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/products")
        {
            Content = JsonContent.Create(new CreateProductRequest(name, 9.99m)),
        };
        request.Headers.Add(TenantResolutionMiddleware.TenantHostHeader, tenant);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new("Bearer", accessToken);
        }

        var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductDto>(Ct))!;
    }

    private static async Task<List<string>> ListProductsAsync(HttpClient client, string tenant, string? accessToken = null)
    {
        var response = await SendAsync(client, HttpMethod.Get, "/api/products", tenant, accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<ProductDto>>(Ct))!.Select(p => p.Name).ToList();
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, string tenant, string? accessToken = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TenantResolutionMiddleware.TenantHostHeader, tenant);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new("Bearer", accessToken);
        }

        return await client.SendAsync(request, Ct);
    }
}
