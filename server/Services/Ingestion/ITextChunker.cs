namespace DocumentAssistant.Services.Ingestion;

/// <summary>A chunk ready to be embedded, with everything needed to cite it later.</summary>
public sealed record TextChunk(int Index, string Content, int? PageNumber);

public interface ITextChunker
{
    /// <summary>Splits extracted segments into overlapping, boundary-aware chunks.</summary>
    IReadOnlyList<TextChunk> Chunk(IReadOnlyList<TextSegment> segments);
}
