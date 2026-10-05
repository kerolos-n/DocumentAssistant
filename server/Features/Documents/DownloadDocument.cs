using System.Security.Claims;
using DocumentAssistant.Common.CQRS;
using DocumentAssistant.Common.Endpoints;
using DocumentAssistant.Data;
using DocumentAssistant.Services;
using Microsoft.EntityFrameworkCore;

namespace DocumentAssistant.Features.Documents;

internal static class DownloadDocument
{
    public sealed record DocumentDownloadResponse(Stream Content, string FileName, string ContentType);

    internal sealed record Query(Guid Id, string? UserId) : IQuery<DocumentDownloadResponse?>;

    internal sealed class Handler(ApplicationDbContext db, DocumentStorage storage)
        : IQueryHandler<Query, DocumentDownloadResponse?>
    {
        public async Task<DocumentDownloadResponse?> Handle(
            Query query,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query.UserId))
            {
                return null;
            }

            // Scoped by owner: someone else's document is indistinguishable from a missing one.
            var document = await db.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    candidate => candidate.Id == query.Id && candidate.UserId == query.UserId,
                    cancellationToken);

            if (document is null)
            {
                return null;
            }

            // Only open the blob once the row proves the caller owns it.
            var content = await storage.OpenReadAsync(document.BlobName, cancellationToken);
            return new DocumentDownloadResponse(content, document.FileName, document.ContentType);
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/api/documents/{id:guid}/download", async (
                Guid id,
                ClaimsPrincipal principal,
                IQueryHandler<Query, DocumentDownloadResponse?> handler,
                CancellationToken cancellationToken) =>
            {
                // The owner comes from the token, never from the URL.
                var query = new Query(id, principal.FindFirstValue(ClaimTypes.NameIdentifier));
                var download = await handler.Handle(query, cancellationToken);

                // Results.File sets Content-Disposition and encodes non-ASCII file names for us.
                return download is null
                    ? Results.NotFound()
                    : Results.File(download.Content, download.ContentType, download.FileName);
            }).RequireAuthorization();
        }
    }
}
