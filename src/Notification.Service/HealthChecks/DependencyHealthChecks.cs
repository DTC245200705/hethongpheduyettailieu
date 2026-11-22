using Microsoft.Extensions.Diagnostics.HealthChecks;
using Notification.Service.Messaging;
using Npgsql;

namespace Notification.Service.HealthChecks;

public sealed class PostgresHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connStr = configuration.GetConnectionString("NotificationDb");
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL (notification_db) is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL (notification_db) is unreachable.", ex);
        }
    }
}

public sealed class RabbitMqConsumerHealthCheck(DocumentEventConsumer consumer) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(consumer.IsConnected
            ? HealthCheckResult.Healthy("RabbitMQ consumer is connected and listening.")
            : HealthCheckResult.Degraded("RabbitMQ consumer is reconnecting..."));
    }
}
