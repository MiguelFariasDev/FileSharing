using FileSharing.Application.Common.Errors;

namespace FileSharing.Application.Common.Exceptions;

/// <summary>Always 409 — "the operation is refused given the resource's current state", e.g. a duplicate email.</summary>
public sealed class ConflictException : AppException
{
    public ConflictException(ResourceErrorCode code, string message)
        : base(409, code, ErrorCodeCatalog.Map(code), message)
    {
    }

    public ConflictException(AuthErrorCode code, string message)
        : base(409, code, ErrorCodeCatalog.Map(code), message)
    {
    }

    public ConflictException(FileErrorCode code, string message)
        : base(409, code, ErrorCodeCatalog.Map(code), message)
    {
    }
}
