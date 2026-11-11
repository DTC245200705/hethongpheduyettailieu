namespace Shared.Contracts.Dtos;

public sealed record CreateDocumentRequest(
    string Title,
    string Content,
    string CreatedBy,
    string AssignedApprover
);

public sealed record WorkflowActionRequest(
    string Actor,
    string? Comment
);

public sealed record DocumentHistoryDto(
    Guid Id,
    string FromStatus,
    string ToStatus,
    string Action,
    string Actor,
    string? Comment,
    DateTime CreatedAtUtc
);

public sealed record DocumentResponse(
    Guid Id,
    string Title,
    string Content,
    string CreatedBy,
    string AssignedApprover,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    List<DocumentHistoryDto>? Histories = null
);

public sealed record NotificationDto(
    Guid Id,
    string Recipient,
    Guid DocumentId,
    string DocumentTitle,
    string EventType,
    string Message,
    string Actor,
    string? Comment,
    bool IsRead,
    string CorrelationId,
    DateTime CreatedAtUtc
);
