using LedgerCore.Application.Abstractions.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LedgerCore.Api.Health;

/// <summary>
/// Ready means the API can reach its database: it opens a connection and runs one trivial
/// query. Redis is left out on purpose, because without it balance reads still work, from SQL
/// Server.
/// </summary>
public sealed class DatabaseHealthCheck(IDbConnectionFactory connectionFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The exception is logged by the health check service. The response body only says
            // "Unhealthy", so no connection details reach the caller.
            return HealthCheckResult.Unhealthy("The database is unreachable.", exception);
        }
    }
}
