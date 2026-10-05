using DocumentAssistant.Services.Chat;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DocumentAssistant.Common.Exceptions;

internal sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is ValidationException validationException)
        {
            logger.LogWarning(
                "Validation failed for {Method} {Path}: {Errors}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                string.Join("; ", validationException.Errors.Select(failure => failure.ErrorMessage)));

            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

            return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "One or more validation errors occurred.",
                    Extensions =
                    {
                        // ProblemDetails.Extensions is flattened to the JSON root, so the client
                        // still sees the { errors: { field: [messages] } } shape it already parses.
                        ["errors"] = validationException.Errors
                            .GroupBy(failure => ToCamelCase(failure.PropertyName))
                            .ToDictionary(
                                group => group.Key,
                                group => group.Select(failure => failure.ErrorMessage).ToArray()),
                    },
                },
            });
        }

        var (statusCode, title) = exception switch
        {
            ArgumentException or BadHttpRequestException =>
                (StatusCodes.Status400BadRequest, "Bad Request"),
            UnauthorizedAccessException =>
                (StatusCodes.Status401Unauthorized, "Unauthorized"),
            KeyNotFoundException =>
                (StatusCodes.Status404NotFound, "Not Found"),
            // The assistant's provider is upstream, so its failures are a gateway problem, not a
            // server fault. The exception's message is safe to show and says what the user can do.
            ChatServiceException =>
                (StatusCodes.Status502BadGateway, "The assistant is unavailable."),
            _ =>
                (StatusCodes.Status500InternalServerError, "Internal Server Error"),
        };

        logger.LogError(
            exception,
            "Unhandled exception for {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path);

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = statusCode == StatusCodes.Status500InternalServerError
                    ? "An unexpected error occurred. Please try again later."
                    : exception.Message,
            },
        });
    }

    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
