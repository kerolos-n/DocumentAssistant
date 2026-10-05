using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocumentAssistant.Services.Ingestion;

/// <summary>
/// Embeds chunks with Google's Gemini embedding model over the Generative Language REST API.
/// The request shape is hand-rolled rather than pulled from an SDK, so the service has no
/// package version to keep in step with the framework.
/// </summary>
public sealed class GeminiEmbeddingService(
    IHttpClientFactory httpClientFactory,
    ILogger<GeminiEmbeddingService> logger,
    string model,
    int dimensions,
    int batchSize) : IEmbeddingService
{
    /// <summary>Name of the client configured in <c>DependencyInjection</c> with the API key.</summary>
    public const string HttpClientName = "gemini-embeddings";

    public const string DefaultModel = "gemini-embedding-001";

    /// <summary>Origin of the Generative Language API; the client's base address.</summary>
    public const string BaseAddress = "https://generativelanguage.googleapis.com/";

    /// <summary><c>batchEmbedContents</c> rejects more than 100 inputs per call.</summary>
    public const int MaxBatchSize = 100;

    /// <summary>
    /// Chunks are indexed for retrieval, so they are embedded as documents. Queries will need
    /// their own call using <c>RETRIEVAL_QUERY</c> — mixing the two degrades similarity.
    /// </summary>
    private const string TaskType = "RETRIEVAL_DOCUMENT";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken)
    {
        if (inputs.Count == 0)
        {
            return [];
        }

        var http = httpClientFactory.CreateClient(HttpClientName);
        var vectors = new List<float[]>(inputs.Count);
        var effectiveBatchSize = Math.Clamp(batchSize, 1, MaxBatchSize);

        for (var offset = 0; offset < inputs.Count; offset += effectiveBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var limit = Math.Min(offset + effectiveBatchSize, inputs.Count);
            var requests = new List<EmbedContentRequest>(limit - offset);
            for (var i = offset; i < limit; i++)
            {
                requests.Add(new EmbedContentRequest(
                    // The API wants the fully-qualified resource name here, not the bare id.
                    Model: $"models/{model}",
                    Content: new EmbedContent([new EmbedPart(inputs[i])]),
                    TaskType: TaskType,
                    OutputDimensionality: dimensions));
            }

            var batch = await RequestBatchAsync(http, requests, cancellationToken);

            if (batch.Embeddings.Count != requests.Count)
            {
                throw new DocumentIngestionException(
                    "The embedding provider returned the wrong number of vectors.");
            }

            foreach (var embedding in batch.Embeddings)
            {
                if (embedding.Values.Length != dimensions)
                {
                    // Storing the wrong width would fail deep in Npgsql with a much worse message.
                    throw new DocumentIngestionException(
                        $"The embedding provider returned {embedding.Values.Length}-dimensional "
                        + $"vectors, but the index expects {dimensions}.");
                }

                // Gemini only normalises at full width; shorter (Matryoshka) outputs have to be
                // normalised here or cosine distance comes out skewed.
                vectors.Add(Normalize(embedding.Values));
            }
        }

        return vectors;
    }

    private async Task<BatchEmbedResponse> RequestBatchAsync(
        HttpClient http,
        IReadOnlyList<EmbedContentRequest> requests,
        CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(
            $"v1beta/models/{model}:batchEmbedContents",
            new BatchEmbedRequest(requests),
            SerializerOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // The body often explains 400s (a bad model name, an over-long chunk); log it, but
            // keep it off the user's screen — the status code is the actionable part.
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError(
                "Gemini embedding request failed with HTTP {StatusCode}: {Body}",
                (int)response.StatusCode,
                body);

            throw new DocumentIngestionException(
                $"The embedding provider rejected the request (HTTP {(int)response.StatusCode}).");
        }

        return await response.Content
            .ReadFromJsonAsync<BatchEmbedResponse>(SerializerOptions, cancellationToken)
            ?? throw new DocumentIngestionException("The embedding provider returned an empty response.");
    }

    /// <summary>Scales to unit length so the stored vectors suit cosine distance.</summary>
    private static float[] Normalize(float[] values)
    {
        var sumOfSquares = 0d;
        foreach (var value in values)
        {
            sumOfSquares += value * value;
        }

        if (sumOfSquares == 0d)
        {
            return values;
        }

        var scale = (float)(1d / Math.Sqrt(sumOfSquares));
        for (var i = 0; i < values.Length; i++)
        {
            values[i] *= scale;
        }

        return values;
    }

    private sealed record BatchEmbedRequest(
        [property: JsonPropertyName("requests")] IReadOnlyList<EmbedContentRequest> Requests);

    private sealed record EmbedContentRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("content")] EmbedContent Content,
        [property: JsonPropertyName("taskType")] string TaskType,
        [property: JsonPropertyName("outputDimensionality")] int OutputDimensionality);

    private sealed record EmbedContent(
        [property: JsonPropertyName("parts")] IReadOnlyList<EmbedPart> Parts);

    private sealed record EmbedPart(
        [property: JsonPropertyName("text")] string Text);

    private sealed record BatchEmbedResponse(
        [property: JsonPropertyName("embeddings")] IReadOnlyList<EmbeddingValues> Embeddings);

    private sealed record EmbeddingValues(
        [property: JsonPropertyName("values")] float[] Values);
}
