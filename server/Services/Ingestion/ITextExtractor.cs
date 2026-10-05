namespace DocumentAssistant.Services.Ingestion;

/// <summary>A run of extracted text that shares one source location.</summary>
/// <param name="Text">The text itself, already stripped of layout artefacts.</param>
/// <param name="PageNumber">1-based page for paged formats; null for Markdown and plain text.</param>
public sealed record TextSegment(string Text, int? PageNumber);

/// <summary>
/// Turns one file format's bytes into text. Implementations are registered with the DI container
/// and picked by <see cref="TextExtractorResolver"/>, so adding a format means adding a class.
/// </summary>
public interface ITextExtractor
{
    /// <summary>Whether this extractor handles an extension such as <c>.pdf</c> (lower-case, with the dot).</summary>
    bool CanHandle(string extension);

    Task<IReadOnlyList<TextSegment>> ExtractAsync(Stream content, CancellationToken cancellationToken);
}
