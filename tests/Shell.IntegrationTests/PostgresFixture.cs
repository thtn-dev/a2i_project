using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shell.Core.Data;
using Shell.Identity.Email;
using Testcontainers.PostgreSql;

namespace Shell.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

/// <summary>
/// One PostgreSQL server for all tests: catalog DB <c>shell_catalog</c> (created by the container),
/// shared DB <c>shell_shared</c> and Silo DB <c>shell_shared_tenant_b</c> (created by the migrate command).
/// Token certificates are generated once in a temporary directory shared by every host of the run; emails are captured in
/// <see cref="Emails"/>.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("shell_catalog")
        .Build();

    private readonly string _keysPath = Path.Combine(Path.GetTempPath(), $"shell-test-keys-{Guid.NewGuid():N}");

    public TestEmailSender Emails { get; } = new();

    public string CatalogConnectionString => _container.GetConnectionString();

    public string SharedConnectionString => ForDatabase("shell_shared");

    public string TenantBConnectionString => ForDatabase("shell_shared_tenant_b");

    public string ForDatabase(string database) =>
        new NpgsqlConnectionStringBuilder(CatalogConnectionString) { Database = database }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // Two instances migrating at the same time: the advisory lock must serialise them.
        await using var first = CreateHost();
        await using var second = CreateHost();
        await Task.WhenAll(MigrateAsync(first), MigrateAsync(second));
    }

    /// <param name="settings">Extra configuration of this host (e.g. a low rate limit).</param>
    public ShellHostFactory CreateHost(IReadOnlyDictionary<string, string?>? settings = null) =>
        new(CatalogConnectionString, SharedConnectionString, _keysPath, Emails, settings);

    public static Task MigrateAsync(ShellHostFactory host) =>
        host.Services.GetRequiredService<MigrationRunner>().RunAsync(CancellationToken.None);

    public async Task<long> CountAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    /// <summary>First column of the first row on the catalog DB, as text.</summary>
    public async Task<string?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(CatalogConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (await command.ExecuteScalarAsync())?.ToString();
    }

    public async Task<Guid> TenantIdAsync(string domain)
    {
        await using var connection = new NpgsqlConnection(CatalogConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT tenant_id FROM catalog.tenant_domains WHERE domain = @domain", connection);
        command.Parameters.AddWithValue("domain", domain);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Runs a statement on the catalog DB (test setup: tenants, memberships...).</summary>
    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(CatalogConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
        if (Directory.Exists(_keysPath))
        {
            Directory.Delete(_keysPath, recursive: true);
        }
    }
}

public sealed class ShellHostFactory(
    string catalogConnectionString,
    string sharedConnectionString,
    string keysPath,
    TestEmailSender emails,
    IReadOnlyDictionary<string, string?>? settings)
    : WebApplicationFactory<Program>
{
    public const string AuthHost = "auth.localhost";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Catalog"] = catalogConnectionString,
            ["ConnectionStrings:DefaultShared"] = sharedConnectionString,
            ["Tenancy:AllowHostHeaderOverride"] = "true",
            ["Tenancy:SeedDemoTenants"] = "true",
            ["Identity:Issuer"] = $"http://{AuthHost}/",
            ["Identity:RequireHttps"] = "false",
            ["Identity:Clients:RedirectUris:0"] = "http://{domain}/signin-callback",
            ["Identity:Clients:PostLogoutRedirectUris:0"] = "http://{domain}/",
            ["Identity:Keys:Path"] = keysPath,
            ["Identity:Keys:AutoGenerate"] = "true",
            ["Identity:RateLimitPerMinute"] = "10000", // Every test request comes from the same (unknown) client IP.
        }));
        if (settings is not null)
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IAccountEmailSender>(emails);
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, FakeSocialHandler>(FakeSocialHandler.SchemeName, "Fake", _ => { });
        });
    }
}
