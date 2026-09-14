using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FileSharing.Api.Middleware;

/// <summary>
/// Last-resort safety net for anything that reaches the pipeline as an unhandled exception —
/// every expected failure (validation, not-found, conflict, unauthorized) is already mapped to
/// its own status code by the controllers themselves and never reaches here. Registered via
/// <c>AddExceptionHandler</c>/<c>UseExceptionHandler</c> (Program.cs); the real exception
/// (including its message, which can legitimately contain SQL/connection details from EF Core
/// or AWS SDK internals) is logged server-side only — the client always gets the same generic,
/// detail-free ProblemDetails body, in every environment, so a stack trace or infrastructure
/// detail can never leak regardless of ASPNETCORE_ENVIRONMENT.
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(
            exception,
            "Unhandled exception processing {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Ocorreu um erro inesperado.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1"
            },
            cancellationToken);

        return true;
    }
}
