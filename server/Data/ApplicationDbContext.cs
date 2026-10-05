using DocumentAssistant.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DocumentAssistant.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity configures its own tables here; skipping the base call drops them all.
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Document>(document =>
        {
            document.Property(d => d.UserId).HasMaxLength(450).IsRequired();
            document.Property(d => d.FileName).HasMaxLength(260).IsRequired();
            document.Property(d => d.ContentType).HasMaxLength(200).IsRequired();
            document.Property(d => d.BlobName).HasMaxLength(500).IsRequired();
            document.HasIndex(d => d.UserId);
        });
    }
}
