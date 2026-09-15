namespace FileSharing.Application.Common.Errors;

/// <summary>
/// Every truly unexpected failure (an unhandled exception of any kind — EF Core, AWS SDK,
/// a bug) collapses to <see cref="UnexpectedError"/> before it ever reaches a client; the real
/// exception (type, message, stack trace) is only ever written to the server-side log. Nothing
/// about the underlying infrastructure is ever allowed to leak into the public code/message.
/// </summary>
public enum SystemErrorCode
{
    UnexpectedError,
    ServiceUnavailable
}
