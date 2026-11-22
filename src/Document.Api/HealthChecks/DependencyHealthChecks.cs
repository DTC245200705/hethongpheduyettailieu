using Document.Api.Messaging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Document.Api.HealthChecks;

public sealed class PostgresHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connStr = configuration.GetConnectionString("DocumentDb");
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL (document_db) is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL (document_db) is unreachable.", ex);
        }
    }
}

public sealed class RabbitMqHealthCheck(RabbitMqPublisher publisher) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(publisher.CanConnect()
            ? HealthCheckResult.Healthy("RabbitMQ publisher connection is active.")
            : HealthCheckResult.Degraded("RabbitMQ is temporarily unreachable. Outbox pattern is buffering events safely."));
    }
}
