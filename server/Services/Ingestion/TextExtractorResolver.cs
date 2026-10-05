namespace DocumentAssistant.Services.Ingestion;

/// <summary>
/// Chooses the extractor that claims a document's extension. The upload endpoint already
/// restricts uploads to the supported extensions, so a miss here means the two lists drifted.
/// </summary>
public sealed class TextExtractorResolver(IEnumerable<ITextExtractor> extractors)
{
    public ITextExtractor Resolve(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        return extractors.FirstOrDefault(extractor => extractor.CanHandle(extension))
            ?? throw new DocumentIngestionException(
                $"Files ending in '{extension}' cannot be read.");
    }
}
