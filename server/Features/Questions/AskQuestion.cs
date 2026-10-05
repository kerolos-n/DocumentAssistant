using System.Security.Claims;
using DocumentAssistant.Common.CQRS;
using DocumentAssistant.Common.Endpoints;
using DocumentAssistant.Common.Questions;
using DocumentAssistant.Common.RateLimiting;
using DocumentAssistant.Data;
using DocumentAssistant.Data.Entities;
using DocumentAssistant.Services.Chat;
using DocumentAssistant.Services.Ingestion;
using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace DocumentAssistant.Features.Questions;

internal static class AskQuestion
{
    /// <summary>Longest question accepted; it also bounds what is sent to the provider.</summary>
    private const int MaxQuestionLength = 2000;

    internal sealed record Command(string? Question, string? UserId) : ICommand<QuestionAnswerResponse>;

    /// <summary>
    /// The answer and the chunks it was built from. <see cref="IsAnswerable"/> is false for both
    /// the "no documents" and "nothing relevant" cases, which <see cref="Outcome"/> tells apart.
    /// </summary>
    internal sealed record QuestionAnswerResponse(
        bool IsAnswerable,
        AnswerOutcome Outcome,
        string Answer,
        IReadOnlyList<CitationResponse> Citations);

    internal sealed record CitationResponse(
        Guid DocumentId,
        string FileName,
        int? PageNumber,
        string Snippet);

    internal enum AnswerOutcome
    {
        Answered,
        NoDocuments,
        NoRelevantContext,
    }

    internal sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.UserId)
                .NotEmpty().WithMessage("You must be signed in to ask a question.");

            RuleFor(command => command.Question)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("Enter a question.")
                .MaximumLength(MaxQuestionLength)
                    .WithMessage($"Questions must be {MaxQuestionLength} characters or fewer.");
        }
    }

    internal sealed class Handler(
        ApplicationDbContext db,
        IEmbeddingService embedder,
        IChatService chat,
        RetrievalOptions retrieval,
        ILogger<Handler> logger) : ICommandHandler<Command, QuestionAnswerResponse>
    {
        private const string NoDocumentsMessage =
            "You don't have any processed documents yet. Upload a document and I'll answer "
            + "questions about it.";

        private const string NoAnswerMessage =
            "I don't know. I couldn't find anything in your documents that answers that question.";

        public async Task<QuestionAnswerResponse> Handle(
            Command command,
            CancellationToken cancellationToken)
        {
            var userId = command.UserId!;
            var question = command.Question!.Trim();

            // A user with nothing ingested gets a specific message, and no embedding or model call
            // is spent finding out.
            var hasReadyDocuments = await db.Documents
                .AsNoTracking()
                .AnyAsync(
                    document => document.UserId == userId && document.Status == DocumentStatus.Ready,
                    cancellationToken);

            if (!hasReadyDocuments)
            {
                return NotAnswerable(AnswerOutcome.NoDocuments, NoDocumentsMessage);
            }

            // The question is embedded as a query, not a document: mixing the two task types
            // degrades similarity. The model and width match the chunks, so the vectors compare.
            var queryVector = new Vector(await embedder.EmbedQueryAsync(question, cancellationToken));

            // Owner-scoped and Ready-only, filtering both the chunk and its document so one user's
            // question can never surface another user's content. The distance is computed once and
            // reused for ordering, then turned into a similarity below.
            var candidates = await db.DocumentChunks
                .AsNoTracking()
                .Where(chunk => chunk.UserId == userId)
                .Join(
                    db.Documents
                        .AsNoTracking()
                        .Where(document =>
                            document.UserId == userId && document.Status == DocumentStatus.Ready),
                    chunk => chunk.DocumentId,
                    document => document.Id,
                    (chunk, document) => new
                    {
                        DocumentId = document.Id,
                        document.FileName,
                        chunk.PageNumber,
                        chunk.Content,
                        Distance = chunk.Embedding.CosineDistance(queryVector),
                    })
                .OrderBy(candidate => candidate.Distance)
                .Take(retrieval.TopK)
                .ToListAsync(cancellationToken);

            // The closest candidate's score (pgvector's cosine distance is 1 − similarity). Kept as
            // a named value because it is what turns a refusal into a tunable number.
            var bestSimilarity = candidates.Count == 0
                ? 0d
                : candidates.Max(candidate => 1d - candidate.Distance);

            // Rounded for readability, but left numeric so the structured log still carries a value
            // rather than a pre-formatted string.
            var bestScore = Math.Round(bestSimilarity, 3);

            var relevant = candidates
                .Select(candidate => new RetrievedChunk(
                    candidate.DocumentId,
                    candidate.FileName,
                    candidate.PageNumber,
                    candidate.Content,
                    // pgvector's cosine distance is 1 − cosine similarity.
                    Similarity: 1d - candidate.Distance))
                .Where(chunk => chunk.Similarity >= retrieval.SimilarityThreshold)
                .ToList();

            if (relevant.Count == 0)
            {
                // Nothing cleared the threshold, so there is no grounding — skip the model call
                // rather than let it guess. The best score is logged because the threshold is a
                // tuning knob: without the number behind a refusal, "nothing matched" and "the
                // bar is set too high" look identical from the outside.
                logger.LogInformation(
                    "Question not answered: best similarity {BestSimilarity}, threshold {Threshold}.",
                    bestScore,
                    retrieval.SimilarityThreshold);

                return NotAnswerable(AnswerOutcome.NoRelevantContext, NoAnswerMessage);
            }

            var answer = await chat.CompleteAsync(
                QuestionPrompt.SystemPrompt,
                QuestionPrompt.BuildUserPrompt(question, relevant),
                cancellationToken);

            logger.LogInformation(
                "Answered: {ChunkCount} chunk(s), best similarity {BestSimilarity}.",
                relevant.Count,
                bestScore);

            var citations = relevant
                .Select(chunk => new CitationResponse(
                    chunk.DocumentId,
                    chunk.FileName,
                    chunk.PageNumber,
                    QuestionPrompt.BuildSnippet(chunk.Content)))
                .ToList();

            return new QuestionAnswerResponse(
                IsAnswerable: true,
                Outcome: AnswerOutcome.Answered,
                Answer: answer,
                Citations: citations);
        }

        private static QuestionAnswerResponse NotAnswerable(AnswerOutcome outcome, string message) =>
            new(
                IsAnswerable: false,
                Outcome: outcome,
                Answer: message,
                Citations: []);
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/api/questions", async (
                Command request,
                ClaimsPrincipal principal,
                ICommandHandler<Command, QuestionAnswerResponse> handler,
                CancellationToken cancellationToken) =>
            {
                // The owner comes from the token; a userId in the body is overwritten, never trusted.
                var command = request with
                {
                    UserId = principal.FindFirstValue(ClaimTypes.NameIdentifier),
                };

                return Results.Ok(await handler.Handle(command, cancellationToken));
            })
            .RequireAuthorization()
            // Answering is the most expensive request in the app, so it is capped per user.
            .RequireRateLimiting(RateLimitPolicies.Questions);
        }
    }
}
