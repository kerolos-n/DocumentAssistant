namespace DocumentAssistant.Services.Ingestion;

/// <summary>
/// A failure whose message is safe and useful to show the user. The worker copies it onto the
/// document's <c>ErrorMessage</c>; anything else is logged and reported generically.
/// </summary>
public sealed class DocumentIngestionException(string message) : Exception(message);
