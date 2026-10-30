using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Shell.Core.Data;

/// <summary>Readiness check: the catalog and shared databases answer <c>SELECT 1</c> (Silo tenant DBs are not checked).</summary>
internal sealed class DatabaseHealthCheck(IOptions<ConnectionStringsOptions> connectionStrings) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        foreach (var (name, connectionString) in new[]
                 {
                     ("catalog", connectionStrings.Value.Catalog),
                     ("shared", connectionStrings.Value.DefaultShared),
                 })
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = new NpgsqlCommand("SELECT 1", connection);
                await command.ExecuteScalarAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return HealthCheckResult.Unhealthy($"The {name} database is unreachable.", ex);
            }
        }

        return HealthCheckResult.Healthy();
    }
}
