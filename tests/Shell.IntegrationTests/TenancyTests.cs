using System.Net;
using System.Net.Http.Json;
using Modules.Products.Contracts;
using Shell.Core.Tenancy;

namespace Shell.IntegrationTests;

/// <summary>End to end: migrate command, tenant resolution, Pool vs Silo storage.</summary>
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

        Assert.Equal(2, await db.CountAsync(db.CatalogConnectionString, "SELECT count(*) FROM catalog.tenants"));
        Assert.Equal(1, await db.CountAsync(db.CatalogConnectionString, "SELECT count(*) FROM catalog.tenants WHERE is_isolated_db"));

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
        var nameA = $"A-{Guid.NewGuid()}";
        var nameB = $"B-{Guid.NewGuid()}";

        var createdA = await CreateProductAsync(client, TenantA, nameA);
        await CreateProductAsync(client, TenantB, nameB);

        var seenByA = await ListProductsAsync(client, TenantA);
        var seenByB = await ListProductsAsync(client, TenantB);
        Assert.Contains(nameA, seenByA);
        Assert.DoesNotContain(nameB, seenByA);
        Assert.Contains(nameB, seenByB);
        Assert.DoesNotContain(nameA, seenByB);

        // A's product id is invisible to B.
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"/api/products/{createdA.Id}", TenantB)).StatusCode);

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

    private static async Task<ProductDto> CreateProductAsync(HttpClient client, string tenant, string name)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/products")
        {
            Content = JsonContent.Create(new CreateProductRequest(name, 9.99m)),
        };
        request.Headers.Add(TenantResolutionMiddleware.TenantHostHeader, tenant);

        var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductDto>(Ct))!;
    }

    private static async Task<List<string>> ListProductsAsync(HttpClient client, string tenant)
    {
        var response = await SendAsync(client, HttpMethod.Get, "/api/products", tenant);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<ProductDto>>(Ct))!.Select(p => p.Name).ToList();
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, string tenant)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TenantResolutionMiddleware.TenantHostHeader, tenant);
        return await client.SendAsync(request, Ct);
    }
}
