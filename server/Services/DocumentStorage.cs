using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace DocumentAssistant.Services;

/// <summary>
/// Wraps a single private blob container. Registered as a singleton because
/// <see cref="BlobContainerClient"/> is thread-safe and holding one connection pool for the
/// process keeps uploads cheap.
/// </summary>
public sealed class DocumentStorage
{
    private readonly BlobContainerClient _container;
    private readonly SemaphoreSlim _containerLock = new(1, 1);
    private bool _containerReady;

    public DocumentStorage(string connectionString, string containerName)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Azure:BlobStorage:ConnectionString must be configured.");
        }

        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new InvalidOperationException(
                "Azure:BlobStorage:ContainerName must be configured.");
        }

        _container = new BlobContainerClient(connectionString, containerName);
    }

    /// <summary>Streams <paramref name="content"/> to <paramref name="blobName"/> and returns the name.</summary>
    public async Task<string> UploadAsync(
        Stream content,
        string blobName,
        string contentType,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        await EnsureContainerAsync(cancellationToken);

        await _container.GetBlobClient(blobName).UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                Metadata = metadata,
            },
            cancellationToken);

        return blobName;
    }

    /// <summary>Best-effort removal, used to roll back a blob when its metadata row cannot be saved.</summary>
    public async Task DeleteAsync(string blobName, CancellationToken cancellationToken)
    {
        await EnsureContainerAsync(cancellationToken);
        await _container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        // Fast path: once the container is known to exist, uploads do not contend on the lock.
        if (_containerReady)
        {
            return;
        }

        await _containerLock.WaitAsync(cancellationToken);
        try
        {
            if (_containerReady)
            {
                return;
            }

            // Idempotent. If it throws — Azurite down, bad credentials — _containerReady stays
            // false, so the next upload retries instead of rethrowing a cached failure forever.
            await _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            _containerReady = true;
        }
        finally
        {
            _containerLock.Release();
        }
    }
}
