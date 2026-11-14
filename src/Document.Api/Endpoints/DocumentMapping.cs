using Document.Api.Entities;
using Shared.Contracts.Dtos;

namespace Document.Api.Endpoints;

public static class DocumentMapping
{
    public static DocumentResponse ToResponse(this DocumentRecord doc, bool includeHistories = false)
    {
        List<DocumentHistoryDto>? histories = null;
        if (includeHistories)
        {
            histories = doc.Histories
                .OrderBy(h => h.CreatedAtUtc)
                .Select(h => new DocumentHistoryDto(
                    h.Id,
                    h.FromStatus,
                    h.ToStatus,
                    h.Action,
                    h.Actor,
                    h.Comment,
                    h.CreatedAtUtc))
                .ToList();
        }

        return new DocumentResponse(
            doc.Id,
            doc.Title,
            doc.Content,
            doc.CreatedBy,
            doc.AssignedApprover,
            doc.Status.ToString(),
            doc.CreatedAtUtc,
            doc.UpdatedAtUtc,
            histories);
    }
}
