using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shell.Core.Data;
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
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("shell_catalog")
        .Build();

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

    public ShellHostFactory CreateHost() => new(CatalogConnectionString, SharedConnectionString);

    public static Task MigrateAsync(ShellHostFactory host) =>
        host.Services.GetRequiredService<MigrationRunner>().RunAsync(CancellationToken.None);

    public async Task<long> CountAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}

public sealed class ShellHostFactory(string catalogConnectionString, string sharedConnectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Catalog"] = catalogConnectionString,
            ["ConnectionStrings:DefaultShared"] = sharedConnectionString,
            ["Tenancy:AllowHostHeaderOverride"] = "true",
            ["Tenancy:SeedDemoTenants"] = "true",
        }));
    }
}
