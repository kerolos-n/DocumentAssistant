using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Reflection;
using DocumentAssistant.Common.Behaviors;
using DocumentAssistant.Common.Ingestion;
using DocumentAssistant.Extensions;
using DocumentAssistant.Features.Auth;
using DocumentAssistant.Data;
using DocumentAssistant.Services;
using DocumentAssistant.Services.Ingestion;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using DocumentAssistant.Common.Exceptions;
using DocumentAssistant.Common.CQRS;
using Pgvector.EntityFrameworkCore;

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
            options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));
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

        // A credentials-free stand-in. Point this at a real provider (and match the vector width
        // to IngestionDefaults.EmbeddingDimensions) to produce semantic embeddings.
        services.AddSingleton<IEmbeddingService>(
            new StubEmbeddingService(IngestionDefaults.EmbeddingDimensions, embeddingBatchSize));

        services.AddSingleton<DocumentIngestionQueue>();
        services.AddHostedService<DocumentIngestionWorker>();
    }
}