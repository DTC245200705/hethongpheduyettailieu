using Microsoft.EntityFrameworkCore;
using Notification.Service.Data;
using Notification.Service.Entities;
using Shared.Contracts;
using Shared.Contracts.Dtos;
using Shared.Contracts.Events;

namespace Notification.Service.Services;

public interface IRealtimeNotifier
{
    Task BroadcastAsync(List<NotificationDto> notifications, CancellationToken ct);
}

public sealed class NoOpRealtimeNotifier : IRealtimeNotifier
{
    public Task BroadcastAsync(List<NotificationDto> notifications, CancellationToken ct) => Task.CompletedTask;
}

public sealed class NotificationDispatcher(
    NotificationDbContext db,
    IRealtimeNotifier realtimeNotifier,
    ILogger<NotificationDispatcher> logger)
{
    public async Task<bool> HandleEventAsync(DocumentStateChangedEvent evt, CancellationToken ct)
    {
        var alreadyProcessed = await db.ProcessedEvents
            .AsNoTracking()
            .AnyAsync(p => p.EventId == evt.EventId, ct);

        if (alreadyProcessed)
        {
            logger.LogInformation(
                "Idempotent skip: Event {EventId} ({EventType}) for Document {DocumentId} was already processed.",
                evt.EventId, evt.EventType, evt.DocumentId);
            return false;
        }

        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(evt.CreatedBy)) recipients.Add(evt.CreatedBy.Trim());
        if (!string.IsNullOrWhiteSpace(evt.AssignedApprover)) recipients.Add(evt.AssignedApprover.Trim());

        var now = DateTime.UtcNow;
        var createdRecords = new List<NotificationRecord>();

        foreach (var recipient in recipients)
        {
            var message = BuildMessage(evt, recipient);
            createdRecords.Add(new NotificationRecord
            {
                Id = Guid.NewGuid(),
                Recipient = recipient,
                DocumentId = evt.DocumentId,
                DocumentTitle = evt.Title,
                EventType = evt.EventType,
                Message = message,
                Actor = evt.Actor,
                Comment = evt.Comment,
                IsRead = false,
                CorrelationId = evt.CorrelationId,
                CreatedAtUtc = now
            });
        }

        db.ProcessedEvents.Add(new ProcessedEvent
        {
            EventId = evt.EventId,
            EventType = evt.EventType,
            CorrelationId = evt.CorrelationId,
            ProcessedAtUtc = now
        });

        db.Notifications.AddRange(createdRecords);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Stored {Count} notification(s) for Event {EventId} ({EventType}) - Document {DocumentId}",
            createdRecords.Count, evt.EventId, evt.EventType, evt.DocumentId);

        var dtos = createdRecords.Select(n => new NotificationDto(
            n.Id,
            n.Recipient,
            n.DocumentId,
            n.DocumentTitle,
            n.EventType,
            n.Message,
            n.Actor,
            n.Comment,
            n.IsRead,
            n.CorrelationId,
            n.CreatedAtUtc)).ToList();

        await realtimeNotifier.BroadcastAsync(dtos, ct);
        return true;
    }

    private static string BuildMessage(DocumentStateChangedEvent evt, string recipient)
    {
        var isApprover = string.Equals(recipient, evt.AssignedApprover, StringComparison.OrdinalIgnoreCase);

        return evt.EventType switch
        {
            MessagingConstants.RoutingKeySubmitted => isApprover
                ? $"[{evt.Actor}] vừa gửi tài liệu \"{evt.Title}\" chờ bạn phê duyệt."
                : $"Tài liệu \"{evt.Title}\" đã được gửi tới [{evt.AssignedApprover}] để phê duyệt.",

            MessagingConstants.RoutingKeyApproved =>
                $"[{evt.Actor}] đã PHÊ DUYỆT tài liệu \"{evt.Title}\".",

            MessagingConstants.RoutingKeyRejected =>
                $"[{evt.Actor}] đã TỪ CHỐI tài liệu \"{evt.Title}\". Lý do: {evt.Comment}",

            _ => $"Tài liệu \"{evt.Title}\" vừa chuyển trạng thái sang {evt.ToStatus}."
        };
    }
}
