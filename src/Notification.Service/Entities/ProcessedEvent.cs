namespace Notification.Service.Entities;

public sealed class ProcessedEvent
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime ProcessedAtUtc { get; set; }
}
