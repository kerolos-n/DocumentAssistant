using UglyToad.PdfPig;

namespace DocumentAssistant.Services.Ingestion;

/// <summary>Extracts text one page at a time, preserving the page number for citations.</summary>
public sealed class PdfTextExtractor : ITextExtractor
{
    private const string Extension = ".pdf";

    public bool CanHandle(string extension) => extension == Extension;

    public Task<IReadOnlyList<TextSegment>> ExtractAsync(
        Stream content,
        CancellationToken cancellationToken)
    {
        // PdfPig's reader is synchronous, so run it off the worker loop rather than blocking it.
        return Task.Run<IReadOnlyList<TextSegment>>(() =>
        {
            var segments = new List<TextSegment>();

            using var document = PdfDocument.Open(content);
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var text = page.Text;
                // A page with no text layer (a scan) yields nothing; the caller turns an
                // entirely empty result into a clear failure further up the pipeline.
                if (!string.IsNullOrWhiteSpace(text))
                {
                    segments.Add(new TextSegment(text, page.Number));
                }
            }

            return segments;
        }, cancellationToken);
    }
}
