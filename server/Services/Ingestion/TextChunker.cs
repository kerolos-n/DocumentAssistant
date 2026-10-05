namespace DocumentAssistant.Services.Ingestion;

/// <summary>
/// Splits text by size with overlap, preferring paragraph then sentence then word boundaries so
/// a chunk rarely starts or ends mid-thought. Segments are chunked independently, which keeps a
/// chunk's page number unambiguous.
/// </summary>
public sealed class TextChunker(int chunkSize, int chunkOverlap) : ITextChunker
{
    public IReadOnlyList<TextChunk> Chunk(IReadOnlyList<TextSegment> segments)
    {
        var chunks = new List<TextChunk>();
        var index = 0;

        foreach (var segment in segments)
        {
            var text = segment.Text;
            var start = 0;

            while (start < text.Length)
            {
                var end = Math.Min(start + chunkSize, text.Length);
                if (end < text.Length)
                {
                    end = FindBoundary(text, start, end);
                }

                var content = text[start..end].Trim();
                if (content.Length > 0)
                {
                    chunks.Add(new TextChunk(index++, content, segment.PageNumber));
                }

                if (end >= text.Length)
                {
                    break;
                }

                // Overlap repeats a little context across the seam; `start + 1` guarantees
                // forward progress even when the overlap is larger than the chunk.
                start = Math.Max(end - chunkOverlap, start + 1);
            }
        }

        return chunks;
    }

    /// <summary>
    /// Walks back from the size limit to the nearest paragraph break, then sentence end, then
    /// space. A boundary in the first half of the window is ignored — it would waste too much.
    /// </summary>
    private static int FindBoundary(string text, int start, int end)
    {
        var minimum = Math.Min(start + ((end - start) / 2), end - 1);
        var length = end - start;

        var paragraph = text.LastIndexOf('\n', end - 1, length);
        if (paragraph >= minimum)
        {
            return paragraph + 1;
        }

        var sentence = text.LastIndexOf(". ", end - 1, length, StringComparison.Ordinal);
        if (sentence >= minimum)
        {
            return sentence + 2;
        }

        var space = text.LastIndexOf(' ', end - 1, length);
        return space >= minimum ? space + 1 : end;
    }
}
