using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DocumentAssistant.Services.Ingestion;

/// <summary>Flattens a DOCX body to text, one paragraph per line.</summary>
public sealed class DocxTextExtractor : ITextExtractor
{
    private const string Extension = ".docx";

    public bool CanHandle(string extension) => extension == Extension;

    public Task<IReadOnlyList<TextSegment>> ExtractAsync(
        Stream content,
        CancellationToken cancellationToken)
    {
        // OpenXml is synchronous and CPU-bound, so keep it off the worker loop.
        return Task.Run<IReadOnlyList<TextSegment>>(() =>
        {
            using var document = WordprocessingDocument.Open(content, isEditable: false);
            var body = document.MainDocumentPart?.Document?.Body;
            if (body is null)
            {
                return [];
            }

            var text = new StringBuilder();
            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                // A paragraph's runs are separate elements; concatenating them restores the line.
                var paragraphText = string.Concat(paragraph.Descendants<Text>().Select(run => run.Text));
                if (!string.IsNullOrWhiteSpace(paragraphText))
                {
                    text.AppendLine(paragraphText);
                }
            }

            var extracted = text.ToString();

            // DOCX has no fixed pagination, so there is no page number to carry.
            return string.IsNullOrWhiteSpace(extracted) ? [] : [new TextSegment(extracted, null)];
        }, cancellationToken);
    }
}
