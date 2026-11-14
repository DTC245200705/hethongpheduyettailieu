using Document.Api.Data;
using Document.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts;
using Shared.Contracts.Dtos;

namespace Document.Api.Endpoints;

public static class DocumentEndpoints
{
    public static RouteGroupBuilder MapDocumentCrudEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/documents").WithTags("Documents");

        group.MapPost("/", async (
            CreateDocumentRequest request,
            DocumentDbContext db,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("DocumentEndpoints");

            if (string.IsNullOrWhiteSpace(request.Title) ||
                string.IsNullOrWhiteSpace(request.Content) ||
                string.IsNullOrWhiteSpace(request.CreatedBy) ||
                string.IsNullOrWhiteSpace(request.AssignedApprover))
            {
                return Results.BadRequest(new
                {
                    error = "Title, Content, CreatedBy, and AssignedApprover are required."
                });
            }

            var now = DateTime.UtcNow;
            var doc = new DocumentRecord
            {
                Id = Guid.NewGuid(),
                Title = request.Title.Trim(),
                Content = request.Content.Trim(),
                CreatedBy = request.CreatedBy.Trim(),
                AssignedApprover = request.AssignedApprover.Trim(),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            var history = new DocumentHistory
            {
                Id = Guid.NewGuid(),
                DocumentId = doc.Id,
                FromStatus = "None",
                ToStatus = DocumentStatus.Draft.ToString(),
                Action = "Created",
                Actor = doc.CreatedBy,
                Comment = "Khởi tạo hồ sơ tài liệu mới.",
                CreatedAtUtc = now
            };

            doc.Histories.Add(history);
            db.Documents.Add(doc);
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Created document {DocumentId} by {CreatedBy}, assigned to {AssignedApprover}",
                doc.Id, doc.CreatedBy, doc.AssignedApprover);

            return Results.Created($"/documents/{doc.Id}", doc.ToResponse(includeHistories: true));
        })
        .WithName("CreateDocument")
        .WithSummary("Tạo tài liệu hoặc hồ sơ mới ở trạng thái Draft");

        group.MapGet("/", async (
            string? status,
            string? approver,
            string? createdBy,
            string? search,
            DocumentDbContext db,
            CancellationToken ct) =>
        {
            var query = db.Documents.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<DocumentStatus>(status, ignoreCase: true, out var parsedStatus))
                {
                    return Results.BadRequest(new
                    {
                        error = $"Invalid status '{status}'. Supported values: Draft, Submitted, Approved, Rejected."
                    });
                }
                query = query.Where(d => d.Status == parsedStatus);
            }

            if (!string.IsNullOrWhiteSpace(approver))
            {
                var normalized = approver.Trim().ToLower();
                query = query.Where(d => d.AssignedApprover.ToLower() == normalized);
            }

            if (!string.IsNullOrWhiteSpace(createdBy))
            {
                var normalized = createdBy.Trim().ToLower();
                query = query.Where(d => d.CreatedBy.ToLower() == normalized);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var pattern = $"%{search.Trim()}%";
                query = query.Where(d =>
                    EF.Functions.ILike(d.Title, pattern) ||
                    EF.Functions.ILike(d.Content, pattern));
            }

            var documents = await query
                .OrderByDescending(d => d.UpdatedAtUtc)
                .ToListAsync(ct);

            return Results.Ok(documents.Select(d => d.ToResponse()).ToList());
        })
        .WithName("GetDocuments")
        .WithSummary("Tra cứu danh sách tài liệu theo trạng thái, người duyệt hoặc người tạo");

        group.MapGet("/{id:guid}", async (
            Guid id,
            DocumentDbContext db,
            CancellationToken ct) =>
        {
            var doc = await db.Documents
                .AsNoTracking()
                .Include(d => d.Histories)
                .FirstOrDefaultAsync(d => d.Id == id, ct);

            return doc is null
                ? Results.NotFound(new { error = $"Document {id} not found." })
                : Results.Ok(doc.ToResponse(includeHistories: true));
        })
        .WithName("GetDocumentById")
        .WithSummary("Lấy chi tiết tài liệu kèm lịch sử các bước xử lý");

        return group;
    }
}
