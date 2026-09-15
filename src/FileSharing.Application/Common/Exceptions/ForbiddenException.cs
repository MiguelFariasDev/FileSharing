using FileSharing.Application.Common.Errors;

namespace FileSharing.Application.Common.Exceptions;

/// <summary>"You are authenticated but not allowed to do this" — always 403.</summary>
public sealed class ForbiddenException : AppException
{
    public ForbiddenException(AuthorizationErrorCode code, string message)
        : base(403, code, ErrorCodeCatalog.Map(code), message)
    {
    }
}
