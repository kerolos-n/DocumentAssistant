namespace DocumentAssistant.Services.Ingestion;

/// <summary>
/// Turns text into vectors. Implementations own their provider's batching and retry policy, so
/// callers can hand over every chunk of a document in one call.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Embeds chunks for indexing. Returns one vector per input, in the same order. Uses the
    /// provider's document task type, which is what makes the stored vectors retrievable.
    /// </summary>
    Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken);

    /// <summary>
    /// Embeds a search query with the provider's query task type. Document and query embeddings
    /// are deliberately different task types; mixing them degrades similarity, so the distinction
    /// is part of the contract rather than a parameter a caller could get wrong.
    /// </summary>
    Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken);
}
