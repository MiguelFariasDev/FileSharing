namespace FileSharing.Web.Models;

/// <summary>
/// Every HTTP failure FileSharingApiClient can produce is normalized into one of these — no
/// component ever branches on a raw HttpStatusCode, and none of them carry the raw response
/// body (which could be a ProblemDetails with internal detail) into user-facing text.
/// </summary>
public enum ApiErrorType
{
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    TooManyRequests,
    ValidationFailed,
    ServerError,
    Network
}
