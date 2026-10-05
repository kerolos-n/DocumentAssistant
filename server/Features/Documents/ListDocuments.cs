using System.Security.Claims;
using DocumentAssistant.Common.CQRS;
using DocumentAssistant.Common.Documents;
using DocumentAssistant.Common.Endpoints;
using DocumentAssistant.Data;
using Microsoft.EntityFrameworkCore;

namespace DocumentAssistant.Features.Documents;

internal static class ListDocuments
{
    internal sealed record Query(string? UserId) : IQuery<IReadOnlyList<DocumentResponse>>;

    internal sealed class Handler(ApplicationDbContext db)
        : IQueryHandler<Query, IReadOnlyList<DocumentResponse>>
    {
        public async Task<IReadOnlyList<DocumentResponse>> Handle(
            Query query,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query.UserId))
            {
                return [];
            }

            var userId = query.UserId;
            return await db.Documents
                .AsNoTracking()
                .Where(document => document.UserId == userId)
                .OrderByDescending(document => document.UploadedAtUtc)
                .Select(document => new DocumentResponse(
                    document.Id,
                    document.FileName,
                    document.ContentType,
                    document.SizeInBytes,
                    document.UploadedAtUtc))
                .ToListAsync(cancellationToken);
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/api/documents", async (
                ClaimsPrincipal principal,
                IQueryHandler<Query, IReadOnlyList<DocumentResponse>> handler,
                CancellationToken cancellationToken) =>
            {
                var query = new Query(principal.FindFirstValue(ClaimTypes.NameIdentifier));
                var documents = await handler.Handle(query, cancellationToken);
                return Results.Ok(documents);
            }).RequireAuthorization();
        }
    }
}
