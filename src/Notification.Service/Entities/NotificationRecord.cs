namespace Notification.Service.Entities;

public sealed class NotificationRecord
{
    public Guid Id { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }
    public string DocumentTitle { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public bool IsRead { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
