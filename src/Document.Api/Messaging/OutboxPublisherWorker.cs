using Document.Api.Data;
using Microsoft.EntityFrameworkCore;
using Serilog.Context;

namespace Document.Api.Messaging;

public sealed class OutboxPublisherWorker(
    IServiceScopeFactory scopeFactory,
    RabbitMqPublisher publisher,
    ILogger<OutboxPublisherWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxPublisherWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in OutboxPublisherWorker loop.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task ProcessPendingMessagesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();

        var pending = await db.OutboxMessages
            .Where(m => m.ProcessedAtUtc == null)
            .OrderBy(m => m.OccurredAtUtc)
            .Take(20)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return;

        foreach (var message in pending)
        {
            using (LogContext.PushProperty("CorrelationId", message.CorrelationId))
            {
                try
                {
                    publisher.Publish(
                        message.RoutingKey,
                        message.Payload,
                        message.EventId,
                        message.CorrelationId);

                    message.ProcessedAtUtc = DateTime.UtcNow;
                    message.LastError = null;

                    logger.LogInformation(
                        "Published Outbox Event {EventId} ({EventType}) to RabbitMQ",
                        message.EventId, message.EventType);
                }
                catch (Exception ex)
                {
                    message.RetryCount++;
                    message.LastError = ex.Message.Length > 950 ? ex.Message[..950] : ex.Message;

                    logger.LogWarning(
                        ex,
                        "Failed to publish Outbox Event {EventId} (Retry #{RetryCount}). Will retry automatically when RabbitMQ recovers.",
                        message.EventId, message.RetryCount);
                    break;
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
