using System.Diagnostics;

namespace DocumentAssistant.Common.Logging;

/// <summary>
/// Gives every request a correlation id and logs its outcome with timing. The id is echoed in a
/// response header and put into a log scope, so a client-reported failure can be traced back to
/// the exact request without guessing. Runs first in the pipeline, which is why its scope also
/// covers the exception handler's logs.
/// </summary>
public sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger)
{
    /// <summary>Both the request and response header the id travels in.</summary>
    public const string HeaderName = "X-Correlation-Id";

    private const int MaxCorrelationIdLength = 128;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);
        // The framework's own log lines pick this up, so they are correlated too.
        context.TraceIdentifier = correlationId;
        // Set before the pipeline runs: the exception handler may write the response first.
        context.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                await next(context);
            }
            finally
            {
                logger.LogInformation(
                    "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds} ms",
                    context.Request.Method,
                    context.Request.Path.Value,
                    context.Response.StatusCode,
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            }
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var header))
        {
            var candidate = header.ToString().Trim();
            // Accept only a bounded, safe token: a client-supplied value ends up in every log line
            // for the request, so it must not carry control characters or unbounded length.
            if (candidate.Length is > 0 and <= MaxCorrelationIdLength &&
                candidate.All(character =>
                    char.IsLetterOrDigit(character) ||
                    character is '-' or '_' or '.'))
            {
                return candidate;
            }
        }

        return Guid.NewGuid().ToString("N");
    }
}
