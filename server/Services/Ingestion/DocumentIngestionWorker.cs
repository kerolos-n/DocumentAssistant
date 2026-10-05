using System.Diagnostics;
using DocumentAssistant.Data;
using DocumentAssistant.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace DocumentAssistant.Services.Ingestion;

/// <summary>
/// Drains the ingestion queue: extract → chunk → embed → store, moving each document from
/// Pending to Ready (or Failed with a reason). Runs serially, so a large upload cannot starve
/// the process of memory or hammer the embedding provider.
/// </summary>
public sealed class DocumentIngestionWorker(
    DocumentIngestionQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<DocumentIngestionWorker> logger) : BackgroundService
{
    /// <summary>Matches the <c>ErrorMessage</c> column's max length.</summary>
    private const int MaxErrorMessageLength = 1000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A crash mid-ingest leaves rows stuck in Pending or Processing; pick them up again.
        await RequeueUnfinishedDocumentsAsync(stoppingToken);

        try
        {
            await foreach (var documentId in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessAsync(documentId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // One bad file must never take the worker down: record and carry on.
                    logger.LogError(exception, "Ingestion failed for document {DocumentId}.", documentId);
                    await TryMarkFailedAsync(documentId, exception);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown; the queue is drained by the host, not by us.
        }
    }

    private async Task ProcessAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();

        // The worker is a singleton, so every unit of work gets its own scope — and with it a
        // fresh DbContext.
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ApplicationDbContext>();

        var document = await db.Documents
            .FirstOrDefaultAsync(candidate => candidate.Id == documentId, cancellationToken);

        if (document is null)
        {
            // Deleted while it sat in the queue; nothing to do.
            return;
        }

        document.Status = DocumentStatus.Processing;
        document.ErrorMessage = null;
        await db.SaveChangesAsync(cancellationToken);

        var storage = services.GetRequiredService<DocumentStorage>();
        await using var content = await storage.OpenReadAsync(document.BlobName, cancellationToken);

        var extractor = services.GetRequiredService<TextExtractorResolver>().Resolve(document.FileName);
        var segments = await extractor.ExtractAsync(content, cancellationToken);

        if (segments.Count == 0)
        {
            throw new DocumentIngestionException(NoTextMessage(document.FileName));
        }

        var chunks = services.GetRequiredService<ITextChunker>().Chunk(segments);
        if (chunks.Count == 0)
        {
            throw new DocumentIngestionException("No readable text was found in this document.");
        }

        var embedder = services.GetRequiredService<IEmbeddingService>();
        var embeddings = await embedder.EmbedDocumentsAsync(
            [.. chunks.Select(chunk => chunk.Content)],
            cancellationToken);

        // Re-ingesting (a retry, or a restart) must replace the old chunks rather than duplicate
        // them.
        await db.DocumentChunks
            .Where(chunk => chunk.DocumentId == documentId)
            .ExecuteDeleteAsync(cancellationToken);

        for (var i = 0; i < chunks.Count; i++)
        {
            db.DocumentChunks.Add(new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                UserId = document.UserId,
                ChunkIndex = chunks[i].Index,
                Content = chunks[i].Content,
                PageNumber = chunks[i].PageNumber,
                Embedding = new Vector(embeddings[i]),
            });
        }

        document.Status = DocumentStatus.Ready;
        document.ErrorMessage = null;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Ingested document {DocumentId} into {ChunkCount} chunks in {ElapsedMilliseconds} ms.",
            documentId,
            chunks.Count,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
    }

    /// <summary>Scanned PDFs are the common case worth naming explicitly — OCR is out of scope.</summary>
    private static string NoTextMessage(string fileName) =>
        Path.GetExtension(fileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            ? "No text could be extracted from this PDF. It may be a scan of images, which this "
                + "app cannot read (OCR is not supported)."
            : "No text could be extracted from this file.";

    /// <summary>Best-effort failure recording, in its own scope so a poisoned context cannot leak.</summary>
    private async Task TryMarkFailedAsync(Guid documentId, Exception exception)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var document = await db.Documents
                .FirstOrDefaultAsync(candidate => candidate.Id == documentId);
            if (document is null)
            {
                return;
            }

            // Only a DocumentIngestionException is written for the user; anything else could leak
            // internals, so it gets a generic message.
            document.Status = DocumentStatus.Failed;
            document.ErrorMessage = Truncate(
                exception is DocumentIngestionException
                    ? exception.Message
                    : "The document could not be processed. Please try uploading it again.");
            await db.SaveChangesAsync();
        }
        catch (Exception markingException)
        {
            logger.LogError(
                markingException,
                "Could not record the ingestion failure for document {DocumentId}.",
                documentId);
        }
    }

    private async Task RequeueUnfinishedDocumentsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var unfinished = await db.Documents
                .Where(document =>
                    document.Status == DocumentStatus.Pending ||
                    document.Status == DocumentStatus.Processing)
                .Select(document => document.Id)
                .ToListAsync(cancellationToken);

            foreach (var documentId in unfinished)
            {
                queue.Enqueue(documentId);
            }

            if (unfinished.Count > 0)
            {
                logger.LogInformation(
                    "Re-queued {Count} unfinished document(s) from a previous run.",
                    unfinished.Count);
            }
        }
        catch (Exception exception)
        {
            // A briefly unavailable database must not stop the app from starting.
            logger.LogError(exception, "Could not re-queue unfinished documents at startup.");
        }
    }

    private static string Truncate(string value) =>
        value.Length <= MaxErrorMessageLength ? value : value[..MaxErrorMessageLength];
}
