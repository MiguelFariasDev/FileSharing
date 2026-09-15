namespace FileSharing.Application.Common.Exceptions;

/// <summary>
/// Base for every "expected" application-level failure that should reach the client as a
/// structured <c>{ code, message, traceId }</c> response instead of a generic 500 — see
/// GlobalExceptionHandler, which is the only place that reads <see cref="HttpStatusCode"/>/
/// <see cref="PublicCode"/>/<see cref="PublicMessage"/>. Never thrown directly: always one of the
/// concrete subclasses below, each constructed from a strongly-typed *ErrorCode enum (never a
/// bare string) via ErrorCodeCatalog, so PublicCode is always one of the catalog's own stable
/// values.
/// </summary>
public abstract class AppException : Exception
{
    public int HttpStatusCode { get; }

    public string PublicCode { get; }

    public string PublicMessage { get; }

    /// <summary>
    /// The originating *ErrorCode enum value, boxed — kept only so tests (and any future
    /// server-side diagnostics) can assert "this exception carries exactly that code" without
    /// re-deriving it from <see cref="PublicCode"/>. Never serialized to the client; the client
    /// only ever sees <see cref="PublicCode"/>.
    /// </summary>
    public Enum Code { get; }

    protected AppException(int httpStatusCode, Enum code, string publicCode, string publicMessage)
        : base(publicMessage)
    {
        HttpStatusCode = httpStatusCode;
        Code = code;
        PublicCode = publicCode;
        PublicMessage = publicMessage;
    }
}
