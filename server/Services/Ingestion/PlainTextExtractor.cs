using System.Text;

namespace DocumentAssistant.Services.Ingestion;

/// <summary>Reads Markdown and plain text, which are already UTF-8 text on disk.</summary>
public sealed class PlainTextExtractor : ITextExtractor
{
    private static readonly string[] SupportedExtensions = [".md", ".txt"];

    public bool CanHandle(string extension) => SupportedExtensions.Contains(extension);

    public async Task<IReadOnlyList<TextSegment>> ExtractAsync(
        Stream content,
        CancellationToken cancellationToken)
    {
        // Honour a byte-order mark if the uploader added one, but assume UTF-8 otherwise.
        using var reader = new StreamReader(
            content,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(cancellationToken);

        // These formats have no pages, so the page number stays null.
        return string.IsNullOrWhiteSpace(text) ? [] : [new TextSegment(text, null)];
    }
}
