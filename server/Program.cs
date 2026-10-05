using System.Text.Json;
using DocumentAssistant.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using DocumentAssistant;

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

app.UseExceptionHandler();
app.UseCors("client");
app.UseAuthentication();
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
