using System.Security.Claims;
using DocumentAssistant.Common.CQRS;
using DocumentAssistant.Common.Documents;
using DocumentAssistant.Common.Endpoints;
using DocumentAssistant.Common.RateLimiting;
using DocumentAssistant.Data;
using DocumentAssistant.Data.Entities;
using DocumentAssistant.Services;
using DocumentAssistant.Services.Ingestion;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DocumentAssistant.Features.Documents;

internal static class UploadDocument
{
    private const long MaxFileSizeInBytes = 20 * 1024 * 1024;

    /// <summary>Per-account ceiling, so one user cannot fill the store unbounded.</summary>
    private const int MaxDocumentsPerUser = 50;

    private static readonly string[] AllowedExtensions = [".pdf", ".docx", ".md", ".txt"];

    internal sealed record Command(IFormFile? File, string? UserId) : ICommand<DocumentResponse>;

    internal sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.UserId)
                .NotEmpty().WithMessage("You must be signed in to upload a document.");

            RuleFor(command => command.File)
                .Cascade(CascadeMode.Stop)
                .NotNull().WithMessage("Choose a file to upload.")
                .Must(file => file!.Length > 0).WithMessage("The file is empty.")
                .Must(file => file!.Length <= MaxFileSizeInBytes)
                    .WithMessage("Files must be 20 MB or smaller.")
                .Must(file => file!.FileName.Length <= 255)
                    .WithMessage("The file name is too long.")
                // The extension is what we trust: browsers report unreliable content types.
                .Must(file => AllowedExtensions.Contains(
                    Path.GetExtension(file!.FileName).ToLowerInvariant()))
                    .WithMessage("Choose a PDF, DOCX, MD, or TXT file.");
        }
    }

    internal sealed class Handler(
        ApplicationDbContext db,
        DocumentStorage storage,
        DocumentIngestionQueue ingestionQueue,
        ILogger<Handler> logger) : ICommandHandler<Command, DocumentResponse>
    {
        public async Task<DocumentResponse> Handle(Command command, CancellationToken cancellationToken)
        {
            var file = command.File!;
            var userId = command.UserId!;
            var fileName = Path.GetFileName(file.FileName);

            // Checked before the blob is written: refusing an over-limit upload must not leave an
            // orphan object behind. The message surfaces to the client as a 400 validation error.
            var existingDocuments = await db.Documents.CountAsync(
                document => document.UserId == userId,
                cancellationToken);
            if (existingDocuments >= MaxDocumentsPerUser)
            {
                throw new ValidationException(
                [
                    new ValidationFailure(
                        "file",
                        $"You have reached the maximum of {MaxDocumentsPerUser} documents. "
                        + "Delete one before uploading another."),
                ]);
            }

            var id = Guid.NewGuid();
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            // The client-supplied name never reaches the path; userId and a fresh GUID do.
            var blobName = $"{userId}/{id}{extension}";
            var contentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType;

            await using (var stream = file.OpenReadStream())
            {
                await storage.UploadAsync(
                    stream,
                    blobName,
                    contentType,
                    // Metadata values must be plain ASCII, so the original file name lives in
                    // Postgres only — "résumé.pdf" would be rejected by the storage service.
                    new Dictionary<string, string>
                    {
                        ["documentId"] = id.ToString(),
                        ["userId"] = userId,
                    },
                    cancellationToken);
            }

            var uploadedAtUtc = DateTime.UtcNow;
            db.Documents.Add(new Document
            {
                Id = id,
                UserId = userId,
                FileName = fileName,
                ContentType = contentType,
                SizeInBytes = file.Length,
                BlobName = blobName,
                UploadedAtUtc = uploadedAtUtc,
                Status = DocumentStatus.Pending,
            });

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                // Never leave a blob behind with no row pointing at it.
                await TryDeleteBlobAsync(blobName, cancellationToken);
                throw;
            }

            // Only once the row is committed: the worker looks the document up by id, so an
            // earlier enqueue would race the insert.
            ingestionQueue.Enqueue(id);

            return new DocumentResponse(
                id,
                fileName,
                contentType,
                file.Length,
                uploadedAtUtc,
                DocumentStatus.Pending,
                null);
        }

        private async Task TryDeleteBlobAsync(string blobName, CancellationToken cancellationToken)
        {
            try
            {
                await storage.DeleteAsync(blobName, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Could not roll back blob {BlobName} after its metadata failed to save.",
                    blobName);
            }
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/api/documents", async (
                IFormFile file,
                ClaimsPrincipal principal,
                ICommandHandler<Command, DocumentResponse> handler,
                CancellationToken cancellationToken) =>
            {
                // The owner comes from the token, never from the request body.
                var command = new Command(file, principal.FindFirstValue(ClaimTypes.NameIdentifier));
                var document = await handler.Handle(command, cancellationToken);
                return Results.Ok(document);
            })
            .RequireAuthorization()
            // Uploads are capped per user so one account cannot flood storage or the ingestion queue.
            .RequireRateLimiting(RateLimitPolicies.Uploads)
            // Auth is JWT bearer, not cookies, so antiforgery adds nothing here — and binding
            // IFormFile without disabling it makes the app throw during startup.
            .DisableAntiforgery();
        }
    }
}
