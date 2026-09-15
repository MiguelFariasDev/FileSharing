using FileSharing.Api.Options;
using FileSharing.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FileSharing.Api.Middleware;

/// <summary>
/// The single place any exception — expected (<see cref="AppException"/>: invalid credentials,
/// a not-found resource, an expired password-reset token...) or truly unexpected (an EF Core/AWS
/// SDK/bug exception) — turns into a response. Registered via
/// <c>AddExceptionHandler</c>/<c>UseExceptionHandler</c> (Program.cs).
///
/// An <see cref="AppException"/> already knows its own HTTP status, public code and safe public
/// message (see that type's remarks) — this handler's only job for that branch is to write it
/// out. Anything else is logged in full server-side (its real type/message/stack trace can
/// legitimately contain SQL/connection details from EF Core or AWS SDK internals) and collapsed
/// to a generic <c>INTERNAL_ERROR</c> the client sees in every environment, so an infrastructure
/// detail can never leak regardless of ASPNETCORE_ENVIRONMENT.
///
/// Writes through <see cref="IProblemDetailsService"/> (rather than a raw
/// <c>WriteAsJsonAsync</c>) specifically so the <c>CustomizeProblemDetails</c> callback
/// registered alongside <c>AddProblemDetails()</c> (see ErrorHandlingExtensions) — which stamps
/// the request's correlation id and the stable public "code" onto every ProblemDetails response,
/// not just this one — also applies here.
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

        // ExceptionHandlerMiddleware resets the response (clearing any headers already set,
        // including CorrelationIdMiddleware's) before invoking this handler, so the header has
        // to be reattached here — HttpContext.Items survives that reset, which is exactly why
        // GetCorrelationId reads from there rather than from a response header that may no
        // longer exist by this point.
        httpContext.Response.Headers.TryAdd(CorrelationIdMiddleware.HeaderName, correlationId);

        var problemDetails = exception is AppException appException
            ? BuildExpectedFailureResponse(httpContext, appException, correlationId)
            : BuildUnexpectedFailureResponse(httpContext, exception, correlationId);

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }

    /// <summary>
    /// An <see cref="AppException"/> is never logged as an error — a wrong password or an
    /// expired reset token is normal, expected traffic, not an incident. A single Information
    /// line (no request body, no password, no token) is enough to correlate with a support
    /// request via the same correlation id the response itself carries.
    /// </summary>
    private ProblemDetails BuildExpectedFailureResponse(HttpContext httpContext, AppException appException, string correlationId)
    {
        _logger.LogInformation(
            "Request rejected with {PublicCode} processing {Method} {Path}. CorrelationId={CorrelationId}",
            appException.PublicCode,
            httpContext.Request.Method,
            httpContext.Request.Path,
            correlationId);

        httpContext.Response.StatusCode = appException.HttpStatusCode;

        return new ProblemDetails
        {
            Status = appException.HttpStatusCode,
            Title = appException.PublicMessage
        };
    }

    private ProblemDetails BuildUnexpectedFailureResponse(HttpContext httpContext, Exception exception, string correlationId)
    {
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

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Ocorreu um erro inesperado. Tente novamente.",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1"
        };

        // Opt-in, local-developer-only convenience (see ObservabilityOptions.EnableDetailedErrors)
        // — the exception's own Message only, never a stack trace, and never in any checked-in
        // appsettings.json.
        if (_observabilityOptions.EnableDetailedErrors)
            problemDetails.Detail = exception.Message;

        return problemDetails;
    }
}
