namespace DocumentAssistant.Common.Questions;

/// <summary>
/// How retrieval behaves before the answer is generated: how many chunks to consider, and how
/// similar a chunk must be to count as context. Bound from the <c>Retrieval</c> configuration
/// section in <c>DependencyInjection</c>, mirroring how the chunker takes its size and overlap.
/// </summary>
public sealed record RetrievalOptions(int TopK, double SimilarityThreshold)
{
    /// <summary>Chunks considered per question. Small, because every one is sent to the model.</summary>
    public const int DefaultTopK = 5;

    /// <summary>
    /// Cosine similarity (1 − pgvector's cosine distance) a chunk must reach. Below this the
    /// chunk is treated as unrelated, so the app answers "I don't know" instead of guessing.
    /// Kept deliberately low: it is a floor for "obviously unrelated", not a relevance test.
    /// Vectors are truncated to 1536 of the model's 3072 dimensions, and truncation deflates
    /// cosine scores, so a bar tuned for full-width embeddings rejects genuinely relevant
    /// chunks. Top-K already does the ranking; this only catches a question that matches
    /// nothing at all. Tune it against the score logged on the "I don't know" path.
    /// </summary>
    public const double DefaultSimilarityThreshold = 0.5;
}
