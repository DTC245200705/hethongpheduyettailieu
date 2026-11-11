namespace Shared.Contracts.Events;

public sealed record DocumentStateChangedEvent(
    Guid EventId,
    string CorrelationId,
    string EventType,
    Guid DocumentId,
    string Title,
    string CreatedBy,
    string AssignedApprover,
    string Actor,
    string FromStatus,
    string ToStatus,
    string? Comment,
    DateTime OccurredAtUtc
);
