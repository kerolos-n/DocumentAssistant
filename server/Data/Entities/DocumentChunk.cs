using Pgvector;

namespace DocumentAssistant.Data.Entities;

public sealed class DocumentChunk
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public int ChunkIndex { get; set; }

    public string Content { get; set; } = string.Empty;

    public int? PageNumber { get; set; }

    public Vector Embedding { get; set; } = null!;
}
