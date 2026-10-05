namespace DocumentAssistant.Services.Ingestion;

/// <summary>
/// A stand-in embedding provider that needs no credentials, so the pipeline runs end to end
/// locally. Each word is hashed into one of the fixed-width buckets, so texts that share
/// vocabulary land near each other under cosine distance.
///
/// This captures lexical overlap, not meaning: it is enough to develop and test retrieval
/// against, but it is not a semantic embedding. Swap in a real provider by registering a
/// different <see cref="IEmbeddingService"/> — the vector width must then still match the
/// <c>vector(1536)</c> column.
/// </summary>
public sealed class StubEmbeddingService(int dimensions, int batchSize) : IEmbeddingService
{
    private const uint FnvOffsetBasis = 2166136261;
    private const uint FnvSignBasis = 16777619;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken)
    {
        var vectors = new List<float[]>(inputs.Count);

        // A real provider's HTTP call has a per-request input cap; mirror that shape here so
        // swapping the implementation does not change how callers behave.
        for (var offset = 0; offset < inputs.Count; offset += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var limit = Math.Min(offset + batchSize, inputs.Count);
            for (var i = offset; i < limit; i++)
            {
                vectors.Add(Embed(inputs[i]));
            }

            // Yield so a long document cannot starve the worker's cancellation checks.
            await Task.Yield();
        }

        return vectors;
    }

    private float[] Embed(string text)
    {
        var vector = new float[dimensions];

        foreach (var token in Tokenize(text))
        {
            var hash = Fnv1a(token, FnvOffsetBasis);
            var index = (int)(hash % (uint)dimensions);
            // A second hash decides the sign, which keeps unrelated words from all pulling in
            // the same direction and inflating every similarity score.
            var sign = (Fnv1a(token, FnvSignBasis) & 1u) == 0 ? 1f : -1f;
            vector[index] += sign;
        }

        Normalize(vector);
        return vector;
    }

    /// <summary>Scales to unit length. A zero vector has no direction, so give it an arbitrary one.</summary>
    private static void Normalize(float[] vector)
    {
        var sumOfSquares = 0d;
        foreach (var value in vector)
        {
            sumOfSquares += value * value;
        }

        if (sumOfSquares == 0d)
        {
            vector[0] = 1f;
            return;
        }

        var scale = (float)(1d / Math.Sqrt(sumOfSquares));
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] *= scale;
        }
    }

    /// <summary>Splits on anything that is not a letter or digit, lower-casing as it goes.</summary>
    private static IEnumerable<string> Tokenize(string text)
    {
        var start = -1;

        for (var i = 0; i <= text.Length; i++)
        {
            var isTokenCharacter = i < text.Length && char.IsLetterOrDigit(text[i]);

            if (isTokenCharacter && start < 0)
            {
                start = i;
            }
            else if (!isTokenCharacter && start >= 0)
            {
                yield return text[start..i].ToLowerInvariant();
                start = -1;
            }
        }
    }

    /// <summary>
    /// FNV-1a over the token's UTF-16 code units. Hand-rolled because
    /// <see cref="string.GetHashCode()"/> is randomised per process and would make stored
    /// vectors meaningless after a restart.
    /// </summary>
    private static uint Fnv1a(string token, uint basis)
    {
        var hash = basis;

        foreach (var character in token)
        {
            hash ^= character;
            hash *= 16777619;
        }

        return hash;
    }
}
