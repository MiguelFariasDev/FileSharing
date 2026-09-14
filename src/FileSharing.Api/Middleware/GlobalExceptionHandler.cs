using FileSharing.Api.Options;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FileSharing.Api.Middleware;

/// <summary>
/// Last-resort safety net for anything that reaches the pipeline as an unhandled exception —
/// every expected failure (validation, not-found, conflict, unauthorized) is already mapped to
/// its own status code by the controllers themselves and never reaches here. Registered via
/// <c>AddExceptionHandler</c>/<c>UseExceptionHandler</c> (Program.cs); the real exception
/// (including its message, which can legitimately contain SQL/connection details from EF Core
/// or AWS SDK internals) is logged server-side only — the client always gets the same generic,
/// detail-free ProblemDetails body by default, in every environment, so a stack trace or
/// infrastructure detail can never leak regardless of ASPNETCORE_ENVIRONMENT.
///
/// Writes through <see cref="IProblemDetailsService"/> (rather than a raw
/// <c>WriteAsJsonAsync</c>) specifically so the <c>CustomizeProblemDetails</c> callback
/// registered alongside <c>AddProblemDetails()</c> in Program.cs — which stamps the request's
/// correlation id onto every ProblemDetails response, not just this one — also applies here.
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ObservabilityOptions _observabilityOptions;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        IProblemDetailsService problemDetailsService,
        IOptions<ObservabilityOptions> observabilityOptions,
        ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _observabilityOptions = observabilityOptions.Value;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var correlationId = httpContext.GetCorrelationId();

        // The exception itself — including its Message, which can legitimately echo back a SQL
        // fragment, a connection detail, or an AWS SDK error — is only ever written to the
        // server-side log, tagged with the same correlation id a support request would quote.
        _logger.LogError(
            exception,
            "Unhandled exception processing {Method} {Path}. CorrelationId={CorrelationId}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            correlationId);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        // ExceptionHandlerMiddleware resets the response (clearing any headers already set,
        // including CorrelationIdMiddleware's) before invoking this handler, so the header has
        // to be reattached here — HttpContext.Items survives that reset, which is exactly why
        // GetCorrelationId reads from there rather than from a response header that may no
        // longer exist by this point.
        httpContext.Response.Headers.TryAdd(CorrelationIdMiddleware.HeaderName, correlationId);

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Ocorreu um erro inesperado.",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1"
        };

        // Opt-in, local-developer-only convenience (see ObservabilityOptions.EnableDetailedErrors)
        // — the exception's own Message only, never a stack trace, and never in any checked-in
        // appsettings.json.
        if (_observabilityOptions.EnableDetailedErrors)
            problemDetails.Detail = exception.Message;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }
}
