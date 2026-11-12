namespace Document.Api.Entities;

public sealed class DocumentHistory
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public DocumentRecord? Document { get; set; }
}
