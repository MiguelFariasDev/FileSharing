using FileSharing.Application.Common.Errors;

namespace FileSharing.Application.Common.Exceptions;

/// <summary>
/// A business-rule violation whose correct HTTP status doesn't fit any of the other, fixed-status
/// exception types above — e.g. an expired password-reset token (410 Gone) or an invalid file
/// upload state transition (400/409 depending on the case). The status is always passed explicitly
/// by the caller (see AppException's own remarks, point 13: the enum itself never encodes a
/// status), defaulting to 400 for the common "malformed/invalid input" case.
/// </summary>
public sealed class DomainException : AppException
{
    public DomainException(AuthErrorCode code, string message, int httpStatusCode)
        : base(httpStatusCode, code, ErrorCodeCatalog.Map(code), message)
    {
    }

    public DomainException(FileErrorCode code, string message, int httpStatusCode = 400)
        : base(httpStatusCode, code, ErrorCodeCatalog.Map(code), message)
    {
    }
}
