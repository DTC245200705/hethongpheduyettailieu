using System.Text.Json;
using Document.Api.Data;
using Document.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts;
using Shared.Contracts.Dtos;
using Shared.Contracts.Events;

namespace Document.Api.Endpoints;

public static class WorkflowEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapDocumentWorkflowEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/submit", async (
            Guid id,
            WorkflowActionRequest request,
            HttpContext httpContext,
            DocumentDbContext db,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("WorkflowEndpoints");
            if (string.IsNullOrWhiteSpace(request.Actor))
            {
                return Results.BadRequest(new { error = "Actor is required." });
            }

            var doc = await db.Documents
                .Include(d => d.Histories)
                .FirstOrDefaultAsync(d => d.Id == id, ct);

            if (doc is null)
                return Results.NotFound(new { error = $"Document {id} not found." });

            if (doc.Status is not (DocumentStatus.Draft or DocumentStatus.Rejected))
            {
                return Results.Conflict(new
                {
                    error = $"Cannot submit document from status '{doc.Status}'. Only Draft or Rejected documents can be submitted."
                });
            }

            if (!string.Equals(doc.CreatedBy, request.Actor.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(
                    new { error = $"Only the creator '{doc.CreatedBy}' can submit this document." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var fromStatus = doc.Status;
            var now = DateTime.UtcNow;
            doc.Status = DocumentStatus.Submitted;
            doc.UpdatedAtUtc = now;

            var comment = string.IsNullOrWhiteSpace(request.Comment)
                ? "Đã gửi tài liệu vào quy trình phê duyệt."
                : request.Comment.Trim();

            var correlationId = GetCorrelationId(httpContext);
            AppendHistoryAndOutbox(
                db, doc, fromStatus, DocumentStatus.Submitted,
                action: "Submitted",
                routingKey: MessagingConstants.RoutingKeySubmitted,
                actor: request.Actor.Trim(),
                comment: comment,
                correlationId: correlationId,
                occurredAtUtc: now);

            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Document {DocumentId} transitioned {FromStatus} -> Submitted by {Actor} [CorrelationId={CorrelationId}]",
                doc.Id, fromStatus, request.Actor, correlationId);

            return Results.Ok(doc.ToResponse(includeHistories: true));
        })
        .WithName("SubmitDocument")
        .WithSummary("Gửi tài liệu vào quy trình phê duyệt (Draft/Rejected -> Submitted)");

        group.MapPost("/{id:guid}/approve", async (
            Guid id,
            WorkflowActionRequest request,
            HttpContext httpContext,
            DocumentDbContext db,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("WorkflowEndpoints");
            if (string.IsNullOrWhiteSpace(request.Actor))
            {
                return Results.BadRequest(new { error = "Actor is required." });
            }

            var doc = await db.Documents
                .Include(d => d.Histories)
                .FirstOrDefaultAsync(d => d.Id == id, ct);

            if (doc is null)
                return Results.NotFound(new { error = $"Document {id} not found." });

            if (doc.Status != DocumentStatus.Submitted)
            {
                return Results.Conflict(new
                {
                    error = $"Cannot approve document in status '{doc.Status}'. Document must be in Submitted status."
                });
            }

            if (!string.Equals(doc.AssignedApprover, request.Actor.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(
                    new { error = $"Only the assigned approver '{doc.AssignedApprover}' can approve this document." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var now = DateTime.UtcNow;
            doc.Status = DocumentStatus.Approved;
            doc.UpdatedAtUtc = now;

            var comment = string.IsNullOrWhiteSpace(request.Comment)
                ? "Tài liệu đã được phê duyệt."
                : request.Comment.Trim();

            var correlationId = GetCorrelationId(httpContext);
            AppendHistoryAndOutbox(
                db, doc, DocumentStatus.Submitted, DocumentStatus.Approved,
                action: "Approved",
                routingKey: MessagingConstants.RoutingKeyApproved,
                actor: request.Actor.Trim(),
                comment: comment,
                correlationId: correlationId,
                occurredAtUtc: now);

            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Document {DocumentId} Approved by {Actor} [CorrelationId={CorrelationId}]",
                doc.Id, request.Actor, correlationId);

            return Results.Ok(doc.ToResponse(includeHistories: true));
        })
        .WithName("ApproveDocument")
        .WithSummary("Phê duyệt tài liệu (Submitted -> Approved)");

        group.MapPost("/{id:guid}/reject", async (
            Guid id,
            WorkflowActionRequest request,
            HttpContext httpContext,
            DocumentDbContext db,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("WorkflowEndpoints");
            if (string.IsNullOrWhiteSpace(request.Actor))
            {
                return Results.BadRequest(new { error = "Actor is required." });
            }

            if (string.IsNullOrWhiteSpace(request.Comment))
            {
                return Results.BadRequest(new { error = "Rejection reason (comment) is required." });
            }

            var doc = await db.Documents
                .Include(d => d.Histories)
                .FirstOrDefaultAsync(d => d.Id == id, ct);

            if (doc is null)
                return Results.NotFound(new { error = $"Document {id} not found." });

            if (doc.Status != DocumentStatus.Submitted)
            {
                return Results.Conflict(new
                {
                    error = $"Cannot reject document in status '{doc.Status}'. Document must be in Submitted status."
                });
            }

            if (!string.Equals(doc.AssignedApprover, request.Actor.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(
                    new { error = $"Only the assigned approver '{doc.AssignedApprover}' can reject this document." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var now = DateTime.UtcNow;
            doc.Status = DocumentStatus.Rejected;
            doc.UpdatedAtUtc = now;

            var correlationId = GetCorrelationId(httpContext);
            AppendHistoryAndOutbox(
                db, doc, DocumentStatus.Submitted, DocumentStatus.Rejected,
                action: "Rejected",
                routingKey: MessagingConstants.RoutingKeyRejected,
                actor: request.Actor.Trim(),
                comment: request.Comment.Trim(),
                correlationId: correlationId,
                occurredAtUtc: now);

            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Document {DocumentId} Rejected by {Actor} [CorrelationId={CorrelationId}]",
                doc.Id, request.Actor, correlationId);

            return Results.Ok(doc.ToResponse(includeHistories: true));
        })
        .WithName("RejectDocument")
        .WithSummary("Từ chối tài liệu kèm lý do (Submitted -> Rejected)");
    }

    private static string GetCorrelationId(HttpContext context)
    {
        if (context.Items.TryGetValue(MessagingConstants.CorrelationIdHeader, out var value) &&
            value is string correlationId &&
            !string.IsNullOrWhiteSpace(correlationId))
        {
            return correlationId;
        }
        return Guid.NewGuid().ToString("N");
    }

    private static void AppendHistoryAndOutbox(
        DocumentDbContext db,
        DocumentRecord doc,
        DocumentStatus fromStatus,
        DocumentStatus toStatus,
        string action,
        string routingKey,
        string actor,
        string? comment,
        string correlationId,
        DateTime occurredAtUtc)
    {
        var history = new DocumentHistory
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            FromStatus = fromStatus.ToString(),
            ToStatus = toStatus.ToString(),
            Action = action,
            Actor = actor,
            Comment = comment,
            CreatedAtUtc = occurredAtUtc
        };

        db.DocumentHistories.Add(history);
        doc.Histories.Add(history);

        var eventId = Guid.NewGuid();
        var integrationEvent = new DocumentStateChangedEvent(
            EventId: eventId,
            CorrelationId: correlationId,
            EventType: routingKey,
            DocumentId: doc.Id,
            Title: doc.Title,
            CreatedBy: doc.CreatedBy,
            AssignedApprover: doc.AssignedApprover,
            Actor: actor,
            FromStatus: fromStatus.ToString(),
            ToStatus: toStatus.ToString(),
            Comment: comment,
            OccurredAtUtc: occurredAtUtc);

        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            EventType = routingKey,
            RoutingKey = routingKey,
            Payload = JsonSerializer.Serialize(integrationEvent, JsonOptions),
            CorrelationId = correlationId,
            OccurredAtUtc = occurredAtUtc,
            RetryCount = 0
        });
    }
}
