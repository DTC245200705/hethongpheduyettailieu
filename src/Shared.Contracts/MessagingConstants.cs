namespace Shared.Contracts;

public static class MessagingConstants
{
    public const string ExchangeName = "doc.events.exchange";
    public const string DeadLetterExchangeName = "doc.events.dlx";
    public const string NotificationQueueName = "notification.doc.queue";
    public const string DeadLetterQueueName = "notification.doc.dlq";

    public const string RoutingKeySubmitted = "document.submitted";
    public const string RoutingKeyApproved = "document.approved";
    public const string RoutingKeyRejected = "document.rejected";
    public const string RoutingKeyWildcard = "document.*";

    public const string CorrelationIdHeader = "X-Correlation-Id";
}
