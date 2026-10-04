using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

// The Angular dev server runs on a different origin, so the browser enforces CORS.
const string ClientDevOrigin = "http://localhost:4200";
builder.Services.AddCors(options => options.AddPolicy("client", policy => policy
    .WithOrigins(ClientDevOrigin)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseCors("client");

app.MapGet("/", () => "Hello World!");

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
