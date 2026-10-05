namespace DocumentAssistant.Services.Ingestion;

/// <summary>
/// Turns text into vectors. Implementations own their provider's batching and retry policy, so
/// callers can hand over every chunk of a document in one call.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>Returns one vector per input, in the same order.</summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken);
}
