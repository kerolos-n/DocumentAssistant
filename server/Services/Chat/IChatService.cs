namespace DocumentAssistant.Services.Chat;

/// <summary>
/// Generates an answer from a system instruction and a user prompt. The provider owns its own
/// request shape, timeout, and error translation, so callers only deal in plain text.
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Returns the model's answer to <paramref name="userPrompt"/>, guided by
    /// <paramref name="systemPrompt"/>. Throws <see cref="ChatServiceException"/> when the
    /// provider fails, times out, or returns nothing usable.
    /// </summary>
    Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken);
}
