using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocumentAssistant.Services.Chat;

/// <summary>
/// Answers questions with Google's Gemini model over the Generative Language REST API. Like the
/// embedding service, the request shape is hand-rolled rather than pulled from an SDK, so there
/// is no package version to keep in step with the framework.
/// </summary>
public sealed class GeminiChatService(
    IHttpClientFactory httpClientFactory,
    ILogger<GeminiChatService> logger,
    IHostEnvironment environment,
    string model,
    int maxOutputTokens) : IChatService
{
    /// <summary>Name of the client configured in <c>DependencyInjection</c> with the API key.</summary>
    public const string HttpClientName = "gemini-chat";

    public const string DefaultModel = "gemini-2.5-flash";

    /// <summary>Origin of the Generative Language API; the client's base address.</summary>
    public const string BaseAddress = "https://generativelanguage.googleapis.com/";

    /// <summary>
    /// Answers are short and grounded in a handful of chunks, so this bounds a runaway response
    /// without truncating a legitimate one.
    /// </summary>
    public const int DefaultMaxOutputTokens = 1024;

    /// <summary>Seconds before the client gives up, so a question cannot hang the request forever.</summary>
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>Low, because a grounded answer should not be creative.</summary>
    private const double Temperature = 0.2;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient(HttpClientName);
        var request = new GenerateContentRequest(
            SystemInstruction: new SystemInstruction([new Part(systemPrompt)]),
            Contents: [new Content("user", [new Part(userPrompt)])],
            GenerationConfig: new GenerationConfig(Temperature, maxOutputTokens));

        try
        {
            using var response = await http.PostAsJsonAsync(
                $"v1beta/models/{model}:generateContent",
                request,
                SerializerOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Status is always logged; the body is not, because it can echo the prompt (which
                // carries the user's document text). It is only logged in Development.
                logger.LogError(
                    "Gemini chat request failed with HTTP {StatusCode}.",
                    (int)response.StatusCode);

                if (environment.IsDevelopment())
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    logger.LogError("Gemini chat error body: {Body}", body);
                }

                throw new ChatServiceException(
                    "The assistant is temporarily unavailable. Please try again.");
            }

            var result = await response.Content.ReadFromJsonAsync<GenerateContentResponse>(
                SerializerOptions,
                cancellationToken)
                ?? throw new ChatServiceException(
                    "The assistant returned an empty response. Please try again.");

            var text = result.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                // A safety block or a non-STOP finish reason both land here; the log has the why.
                logger.LogError(
                    "Gemini chat returned no answer text. Block reason: {BlockReason}; finish reason: {FinishReason}.",
                    result.PromptFeedback?.BlockReason,
                    result.Candidates?.FirstOrDefault()?.FinishReason);

                throw new ChatServiceException(
                    "The assistant could not answer that question. Please try rephrasing it.");
            }

            return text.Trim();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The HttpClient timeout fired rather than the caller aborting, which is a 502 the
            // user can retry — not a cancelled request.
            logger.LogError("Gemini chat request timed out.");
            throw new ChatServiceException("The assistant took too long to respond. Please try again.");
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Gemini chat request could not reach the provider.");
            throw new ChatServiceException("The assistant is temporarily unavailable. Please try again.");
        }
    }

    private sealed record GenerateContentRequest(
        [property: JsonPropertyName("systemInstruction")] SystemInstruction SystemInstruction,
        [property: JsonPropertyName("contents")] IReadOnlyList<Content> Contents,
        [property: JsonPropertyName("generationConfig")] GenerationConfig GenerationConfig);

    /// <summary>System instructions carry no <c>role</c>, so they get their own shape.</summary>
    private sealed record SystemInstruction(
        [property: JsonPropertyName("parts")] IReadOnlyList<Part> Parts);

    private sealed record Content(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("parts")] IReadOnlyList<Part> Parts);

    private sealed record Part(
        [property: JsonPropertyName("text")] string Text);

    private sealed record GenerationConfig(
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("maxOutputTokens")] int MaxOutputTokens);

    private sealed record GenerateContentResponse(
        [property: JsonPropertyName("candidates")] IReadOnlyList<Candidate>? Candidates,
        [property: JsonPropertyName("promptFeedback")] PromptFeedback? PromptFeedback);

    private sealed record Candidate(
        [property: JsonPropertyName("content")] Content? Content,
        [property: JsonPropertyName("finishReason")] string? FinishReason);

    private sealed record PromptFeedback(
        [property: JsonPropertyName("blockReason")] string? BlockReason);
}
