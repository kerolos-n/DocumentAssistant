using System.Text.Json;
using DocumentAssistant.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using DocumentAssistant;
using DocumentAssistant.Common.Logging;

var repositoryDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
while (repositoryDirectory is not null && !File.Exists(Path.Combine(repositoryDirectory.FullName, "server", "server.csproj")))
{
    repositoryDirectory = repositoryDirectory.Parent;
}

var envFilePath = Path.Combine(repositoryDirectory?.FullName ?? Directory.GetCurrentDirectory(), "server", ".env");
if (File.Exists(envFilePath))
{
    DotNetEnv.Env.NoClobber().Load(envFilePath);
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAllServices(builder.Configuration);

var app = builder.Build();

// First in the pipeline, so every request gets a correlation id and its logs and outcome timing
// stay together — including the exception handler's logs.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseCors("client");
app.UseAuthentication();
// After authentication so per-user partitions can read the NameIdentifier claim.
app.UseRateLimiter();
app.UseAuthorization();

app.MapGet("/", () => "Hello World!");
app.MapEndpoints();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    // Respond with JSON so the client can show a meaningful message.
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            durationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
            }),
        }));
    },
});

app.Run();
