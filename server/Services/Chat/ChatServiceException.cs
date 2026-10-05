namespace DocumentAssistant.Services.Chat;

/// <summary>
/// A chat failure whose message is safe to show the user. The endpoint surfaces it as a 502, so
/// the message must never carry provider internals — those are logged instead.
/// </summary>
public sealed class ChatServiceException(string message) : Exception(message);
