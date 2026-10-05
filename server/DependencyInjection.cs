using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using System.Reflection;
using DocumentAssistant.Common.Behaviors;
using DocumentAssistant.Common.Ingestion;
using DocumentAssistant.Common.Questions;
using DocumentAssistant.Common.RateLimiting;
using DocumentAssistant.Extensions;
using DocumentAssistant.Data;
using DocumentAssistant.Services;
using DocumentAssistant.Services.Chat;
using DocumentAssistant.Services.Ingestion;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using DocumentAssistant.Common.Exceptions;
using DocumentAssistant.Common.CQRS;

namespace DocumentAssistant;

public static class DependencyInjection
{
    public static void AddAllServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHealthChecks();

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default must be configured for PostgreSQL.");
        var jwtKey = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException(
                "Jwt:Key must be configured with a secret of at least 32 bytes.");
        var jwtIssuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Jwt:Issuer must be configured.");
        var jwtAudience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("Jwt:Audience must be configured.");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Default must be configured for PostgreSQL.");
        }
        if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
        {
            throw new InvalidOperationException("Jwt:Key must be a secret of at least 32 bytes.");
        }
        if (string.IsNullOrWhiteSpace(jwtIssuer) || string.IsNullOrWhiteSpace(jwtAudience))
        {
            throw new InvalidOperationException("Jwt:Issuer and Jwt:Audience must not be empty.");
        }
        var jwtExpirationMinutes = configuration.GetValue<int>("Jwt:ExpirationMinutes", 60);
        var jwtSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

        var blobConnectionString = configuration["Azure:BlobStorage:ConnectionString"];
        if (string.IsNullOrWhiteSpace(blobConnectionString))
        {
            throw new InvalidOperationException(
                "Azure:BlobStorage:ConnectionString must be configured " +
                "(use 'UseDevelopmentStorage=true' with Azurite locally).");
        }
        var blobContainerName = configuration["Azure:BlobStorage:ContainerName"];
        if (string.IsNullOrWhiteSpace(blobContainerName))
        {
            throw new InvalidOperationException(
                "Azure:BlobStorage:ContainerName must be configured.");
        }

        // UseVector teaches Npgsql to read and write the `vector` type that DocumentChunk.Embedding
        // maps to; without it, inserts fail at runtime rather than at startup.
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql
                .UseVector()
                .EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null)));
        services.AddIdentityCore<IdentityUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
            }).AddEntityFrameworkStores<ApplicationDbContext>();

        services.Scan(scan => scan.FromAssembliesOf(typeof(DependencyInjection))
        .AddClasses(classes => classes.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime()
        .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime()
        .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        services.Decorate(typeof(ICommandHandler<,>), typeof(ValidationBehavior.CommandHandler<,>));
        // services.Decorate(typeof(ICommandHandler<>), typeof(ValidationBehavior.CommandBaseHandler<>));

        // ValidationBehavior resolves IEnumerable<IValidator<T>>. Without this scan every
        // Features/*/Validator is never registered and its rules are silently skipped.
        // includeInternalTypes matches the slices, whose validators are internal nested classes.
        services.AddValidatorsFromAssembly(
            Assembly.GetExecutingAssembly(),
            includeInternalTypes: true);

        services.AddEndpoints(Assembly.GetExecutingAssembly());

        services.AddSingleton(new JwtTokenService(jwtKey, jwtIssuer, jwtAudience, jwtExpirationMinutes));
        services.AddSingleton(new DocumentStorage(blobConnectionString, blobContainerName));

        // Status travels to the client as a readable name ("Processing"), not an ordinal, so the
        // Angular badges can switch on it directly.
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        AddIngestion(services, configuration);
        AddQuestionAnswering(services, configuration);
        AddRateLimiting(services, configuration);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = jwtAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = jwtSigningKey,
                    ValidateLifetime = true,
                    NameClaimType = ClaimTypes.NameIdentifier,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });
        services.AddAuthorization();

        // The Angular dev server runs on a different origin, so the browser enforces CORS.
        var clientOrigin = configuration["Client:Origin"] ?? "http://localhost:4200";
        services.AddCors(options => options.AddPolicy("client", policy => policy
            .WithOrigins(clientOrigin)
            .AllowAnyHeader()
            .AllowAnyMethod()));

        services.AddEndpoints(Assembly.GetExecutingAssembly());
    }

    /// <summary>
    /// Registers the extract → chunk → embed → store pipeline. Extractors are singletons (they
    /// hold no state), the chunker and embedder are configured once from the <c>Ingestion</c>
    /// section, and the worker is a hosted service so it starts and stops with the app.
    /// </summary>
    private static void AddIngestion(IServiceCollection services, IConfiguration configuration)
    {
        var chunkSize = configuration.GetValue("Ingestion:ChunkSize", IngestionDefaults.ChunkSize);
        var chunkOverlap = configuration.GetValue("Ingestion:ChunkOverlap", IngestionDefaults.ChunkOverlap);
        var embeddingBatchSize = configuration.GetValue(
            "Ingestion:EmbeddingBatchSize",
            IngestionDefaults.EmbeddingBatchSize);

        services.AddSingleton<ITextExtractor, PdfTextExtractor>();
        services.AddSingleton<ITextExtractor, DocxTextExtractor>();
        services.AddSingleton<ITextExtractor, PlainTextExtractor>();
        services.AddSingleton<TextExtractorResolver>();

        services.AddSingleton<ITextChunker>(new TextChunker(chunkSize, chunkOverlap));

        AddEmbedding(services, configuration, embeddingBatchSize);

        services.AddSingleton<DocumentIngestionQueue>();
        services.AddHostedService<DocumentIngestionWorker>();
    }

    /// <summary>
    /// Registers the Gemini embedding provider. The API key is required: without it every
    /// upload would fail, so the app refuses to start rather than accept documents it cannot
    /// index — the same contract as the connection string and JWT key.
    /// </summary>
    private static void AddEmbedding(IServiceCollection services, IConfiguration configuration, int batchSize)
    {
        var apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Gemini:ApiKey must be configured to sembed document chunks " + "(set Gemini__ApiKey in server/.env).");
        }

        var model = configuration["Gemini:EmbeddingModel"];
        if (string.IsNullOrWhiteSpace(model))
        {
            model = GeminiEmbeddingService.DefaultModel;
        }

        services.AddHttpClient(GeminiEmbeddingService.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(GeminiEmbeddingService.BaseAddress);
            // A header rather than the ?key= query parameter keeps the secret out of URLs and logs.
            client.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        // The factory is injected rather than a single HttpClient, so the handler is recycled and
        // DNS stays fresh across a long-lived singleton.
        services.AddSingleton<IEmbeddingService>(provider => new GeminiEmbeddingService(
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<ILogger<GeminiEmbeddingService>>(),
            provider.GetRequiredService<IHostEnvironment>(),
            model,
            IngestionDefaults.EmbeddingDimensions,
            batchSize));
    }

    /// <summary>
    /// Registers retrieval knobs and the chat provider that answers questions. Runs after
    /// <see cref="AddIngestion"/>, which has already rejected a missing API key.
    /// </summary>
    private static void AddQuestionAnswering(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var topK = configuration.GetValue("Retrieval:TopK", RetrievalOptions.DefaultTopK);
        var similarityThreshold = configuration.GetValue(
            "Retrieval:SimilarityThreshold",
            RetrievalOptions.DefaultSimilarityThreshold);
        services.AddSingleton(new RetrievalOptions(topK, similarityThreshold));

        var apiKey = configuration["Gemini:ApiKey"]!;

        var model = configuration["Gemini:ChatModel"];
        if (string.IsNullOrWhiteSpace(model))
        {
            model = GeminiChatService.DefaultModel;
        }

        var maxOutputTokens = configuration.GetValue(
            "Gemini:ChatMaxOutputTokens",
            GeminiChatService.DefaultMaxOutputTokens);
        var timeoutSeconds = configuration.GetValue(
            "Gemini:ChatTimeoutSeconds",
            GeminiChatService.DefaultTimeoutSeconds);

        services.AddHttpClient(GeminiChatService.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(GeminiChatService.BaseAddress);
            // A header rather than the ?key= query parameter keeps the secret out of URLs and logs.
            client.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        });

        services.AddSingleton<IChatService>(provider => new GeminiChatService(
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<ILogger<GeminiChatService>>(),
            provider.GetRequiredService<IHostEnvironment>(),
            model,
            maxOutputTokens));
    }

    /// <summary>
    /// Registers the per-user rate-limit policies applied to the two expensive, abusable
    /// endpoints: asking a question and uploading a document. Each partition is one signed-in
    /// user, falling back to the client IP so anonymous callers are still bounded.
    /// </summary>
    private static void AddRateLimiting(IServiceCollection services, IConfiguration configuration)
    {
        var questionPermitLimit = configuration.GetValue("RateLimiting:QuestionPermitLimit", RateLimitPolicies.DefaultQuestionPermitLimit);
        var questionWindowSeconds = configuration.GetValue("RateLimiting:QuestionWindowSeconds", RateLimitPolicies.DefaultQuestionWindowSeconds);
        var uploadPermitLimit = configuration.GetValue("RateLimiting:UploadPermitLimit", RateLimitPolicies.DefaultUploadPermitLimit);
        var uploadWindowSeconds = configuration.GetValue("RateLimiting:UploadWindowSeconds", RateLimitPolicies.DefaultUploadWindowSeconds);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimitPolicies.Questions,
                httpContext => PartitionByUser(httpContext, questionPermitLimit, questionWindowSeconds));
            options.AddPolicy(RateLimitPolicies.Uploads,
                httpContext => PartitionByUser(httpContext, uploadPermitLimit, uploadWindowSeconds));

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                // Tell a well-behaved client how long to wait when the limiter knows.
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                // No secrets or request content here: just who was throttled and where.
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("DocumentAssistant.RateLimiting");
                logger.LogWarning(
                    "Rate limit rejected {Method} {Path} for {PartitionKey}.",
                    context.HttpContext.Request.Method,
                    context.HttpContext.Request.Path.Value,
                    context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous");

                // ProblemDetails so the client's existing error parser shows a readable message
                // rather than an empty body.
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests.",
                        Detail = "You're going too fast. Please try again in a moment.",
                    },
                    cancellationToken);
            };
        });
    }

    /// <summary>
    /// One fixed-window bucket per signed-in user, keyed by the NameIdentifier claim. This runs
    /// after authentication (see Program.cs), so the claim is populated; anonymous requests fall
    /// back to the client IP so they are bounded too. The keys are claim values, not unbounded
    /// request text, so the partition map cannot be grown without bound.
    /// </summary>
    private static RateLimitPartition<string> PartitionByUser(
        HttpContext httpContext,
        int permitLimit,
        int windowSeconds)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var partitionKey = userId is not null
            ? $"user:{userId}"
            : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = permitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromSeconds(windowSeconds),
            });
    }
}