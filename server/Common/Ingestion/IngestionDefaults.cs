namespace DocumentAssistant.Common.Ingestion;

/// <summary>
/// Defaults shared by the ingestion pipeline and the EF model. The chunking and batching values
/// can be overridden under the <c>Ingestion</c> configuration section; the embedding width is
/// baked into the <c>vector(1536)</c> column, so changing it means regenerating the migration.
/// </summary>
public static class IngestionDefaults
{
    /// <summary>
    /// Must match the embedding provider's output width. 1536 is one of the widths
    /// <c>gemini-embedding-001</c> supports, so the model is asked to truncate to it — which keeps
    /// the vectors cheap to store and compare without changing the column.
    /// </summary>
    public const int EmbeddingDimensions = 1536;

    /// <summary>Target characters per chunk — roughly 250 tokens, well inside provider limits.</summary>
    public const int ChunkSize = 1000;

    /// <summary>Characters repeated from the end of one chunk at the start of the next.</summary>
    public const int ChunkOverlap = 200;

    /// <summary>How many chunks are sent to the embedding provider per request.</summary>
    public const int EmbeddingBatchSize = 64;
}
