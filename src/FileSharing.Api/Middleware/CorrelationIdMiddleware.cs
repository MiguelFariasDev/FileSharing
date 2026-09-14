using System.Diagnostics.CodeAnalysis;

namespace FileSharing.Api.Middleware;

/// <summary>
/// Assigns every request a correlation id — reused from the client's own
/// <see cref="HeaderName"/> header when it looks like a real id, otherwise generated fresh —
/// and makes it available three ways: on <see cref="HttpContext.Items"/> (read by
/// <see cref="GlobalExceptionHandler"/> and the ProblemDetails customization in Program.cs),
/// as a logging scope (so every log line emitted while handling this request carries it, for
/// any provider that renders scopes), and echoed back on the response header so a caller can
/// correlate their own logs/support ticket with server-side logs for the same request.
///
/// Registered first in the pipeline (Program.cs) — before UseExceptionHandler — specifically so
/// the id is established, and the response header already attached, even when a downstream
/// middleware/handler throws.
/// </summary>
public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    public const string HttpContextItemKey = "CorrelationId";

    // Generous enough for a UUID, ULID, or similar client-generated id; anything longer is
    // rejected (regenerated) rather than truncated — never persist/log a client-controlled blob
    // of unbounded size just because it arrived under a plausible-looking header name.
    private const int MaxLength = 128;

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        context.Items[HttpContextItemKey] = correlationId;

        // Set before calling next() (and via TryAdd, not the indexer) so it is already attached
        // to the response — and therefore still sent — even if a later middleware throws before
        // ever reaching here again, or if something downstream already added the same header.
        context.Response.Headers.TryAdd(HeaderName, correlationId);

        // The message-template form (rather than a raw Dictionary) so both the console
        // formatter (Logging:Console:FormatterOptions:IncludeScopes, appsettings.json) and any
        // structured backend added later render "CorrelationId" as its own field.
        using (_logger.BeginScope("CorrelationId: {CorrelationId}", correlationId))
        {
            await _next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var values) &&
            IsValidClientCorrelationId(values, out var candidate))
        {
            return candidate;
        }

        return Guid.NewGuid().ToString("n");
    }

    private static bool IsValidClientCorrelationId(Microsoft.Extensions.Primitives.StringValues values, [NotNullWhen(true)] out string? candidate)
    {
        candidate = null;

        // Ambiguous (repeated header) or absent — never guess which one the caller meant.
        if (values.Count != 1)
            return false;

        var value = values[0];
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
            return false;

        // Restricted to a safe, log-and-URL-friendly charset — this value flows into response
        // headers and every log line for the request, so it must never be able to smuggle
        // control characters, delimiters, or anything else that could confuse a log line or a
        // downstream header parser.
        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
                return false;
        }

        candidate = value;
        return true;
    }
}

public static class HttpContextCorrelationIdExtensions
{
    /// <summary>
    /// Reads the id CorrelationIdMiddleware already resolved for this request. Falls back to
    /// ASP.NET Core's own per-request <see cref="HttpContext.TraceIdentifier"/> only if the
    /// middleware somehow never ran (defensive — every real request path registers it in
    /// Program.cs) rather than returning null, since every caller of this method needs some id.
    /// </summary>
    public static string GetCorrelationId(this HttpContext context) =>
        context.Items.TryGetValue(CorrelationIdMiddleware.HttpContextItemKey, out var value) && value is string correlationId
            ? correlationId
            : context.TraceIdentifier;
}
