using DocumentAssistant.Data;
using Microsoft.EntityFrameworkCore;

namespace DocumentAssistant.Extensions;

/// <summary>
/// Applies pending EF Core migrations at startup, so a fresh database — or a checkout that has
/// moved ahead of the schema — becomes usable without a manual <c>dotnet ef database update</c>.
/// </summary>
public static class MigrationExtensions
{
    /// <summary>
    /// Called before the host starts, which is also before <c>DocumentIngestionWorker</c>'s
    /// startup re-queue runs — so that query never hits a table that does not exist yet.
    /// <para>
    /// A failure is left to propagate: serving requests against an unknown schema is worse than
    /// not starting, which is the same contract the missing-configuration checks in
    /// <c>AddAllServices</c> enforce.
    /// </para>
    /// </summary>
    public static async Task ApplyMigrationsAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0)
        {
            app.Logger.LogInformation("Database schema is up to date; no migrations to apply.");
            return;
        }

        app.Logger.LogInformation(
            "Applying {Count} pending migration(s): {Migrations}.",
            pending.Count,
            string.Join(", ", pending));

        await db.Database.MigrateAsync();

        app.Logger.LogInformation("Database migrations applied.");
    }
}
