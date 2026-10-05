using System.Security.Claims;
using DocumentAssistant.Common.CQRS;
using DocumentAssistant.Common.Endpoints;
using DocumentAssistant.Data;
using DocumentAssistant.Services;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DocumentAssistant.Features.Documents;

internal static class DeleteDocument
{
    internal sealed record Command(Guid Id, string? UserId) : ICommand<bool>;

    internal sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.UserId)
                .NotEmpty().WithMessage("You must be signed in to delete a document.");
        }
    }

    internal sealed class Handler(
        ApplicationDbContext db,
        DocumentStorage storage,
        ILogger<Handler> logger) : ICommandHandler<Command, bool>
    {
        public async Task<bool> Handle(Command command, CancellationToken cancellationToken)
        {
            var userId = command.UserId;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return false;
            }

            // Scoped by owner: someone else's document is indistinguishable from a missing one.
            var document = await db.Documents
                .FirstOrDefaultAsync(
                    candidate => candidate.Id == command.Id && candidate.UserId == userId,
                    cancellationToken);

            if (document is null)
            {
                return false;
            }

            // The DocumentChunks foreign key is ON DELETE CASCADE, so removing the row removes
            // the document's chunks in the same transaction — no separate delete needed.
            db.Documents.Remove(document);
            await db.SaveChangesAsync(cancellationToken);

            // The row is gone, so the user's list is already consistent. A failed blob delete
            // would only strand an unreferenced object, which is not worth failing the request over.
            try
            {
                await storage.DeleteAsync(document.BlobName, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Deleted document {DocumentId} but could not remove blob {BlobName}.",
                    document.Id,
                    document.BlobName);
            }

            return true;
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapDelete("/api/documents/{id:guid}", async (
                Guid id,
                ClaimsPrincipal principal,
                ICommandHandler<Command, bool> handler,
                CancellationToken cancellationToken) =>
            {
                var command = new Command(id, principal.FindFirstValue(ClaimTypes.NameIdentifier));
                var deleted = await handler.Handle(command, cancellationToken);
                return deleted ? Results.NoContent() : Results.NotFound();
            }).RequireAuthorization();
        }
    }
}
