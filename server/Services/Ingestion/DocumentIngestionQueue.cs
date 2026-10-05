using System.Threading.Channels;

namespace DocumentAssistant.Services.Ingestion;

/// <summary>
/// Hands document ids from the upload endpoint to the background worker. The channel decouples
/// the two: an upload returns as soon as the row is saved, and the worker drains the queue on
/// its own schedule.
/// </summary>
public sealed class DocumentIngestionQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions
        {
            // One worker reads; many request threads write.
            SingleReader = true,
            SingleWriter = false,
        });

    public void Enqueue(Guid documentId) => _channel.Writer.TryWrite(documentId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
