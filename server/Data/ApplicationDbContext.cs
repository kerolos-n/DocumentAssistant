using DocumentAssistant.Common.Ingestion;
using DocumentAssistant.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DocumentAssistant.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity configures its own tables here; skipping the base call drops them all.
        base.OnModelCreating(modelBuilder);

        // pgvector supplies the `vector` column type. The extension must exist on the server
        // (the `pgvector/pgvector` Postgres image ships it); this makes the migration create it.
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<Document>(document =>
        {
            document.Property(d => d.UserId).HasMaxLength(450).IsRequired();
            document.Property(d => d.FileName).HasMaxLength(260).IsRequired();
            document.Property(d => d.ContentType).HasMaxLength(200).IsRequired();
            document.Property(d => d.BlobName).HasMaxLength(500).IsRequired();
            // Stored as text so the database and the JSON API agree on readable status names.
            // The SQL default backfills rows that predate this column (they get re-ingested).
            document.Property(d => d.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValueSql("'Pending'")
                .IsRequired();
            document.Property(d => d.ErrorMessage).HasMaxLength(1000);
            document.HasIndex(d => d.UserId);
        });

        modelBuilder.Entity<DocumentChunk>(chunk =>
        {
            chunk.Property(c => c.UserId).HasMaxLength(450).IsRequired();
            chunk.Property(c => c.Content).IsRequired();
            chunk.Property(c => c.Embedding)
                .HasColumnType($"vector({IngestionDefaults.EmbeddingDimensions})")
                .IsRequired();

            // Cascade keeps chunks from outliving their document: deleting a row in Documents
            // removes its chunks in the same transaction, so DeleteDocument needs no extra work.
            chunk.HasOne<Document>()
                .WithMany(document => document.Chunks)
                .HasForeignKey(c => c.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            chunk.HasIndex(c => c.DocumentId);
            chunk.HasIndex(c => c.UserId);
        });
    }
}
