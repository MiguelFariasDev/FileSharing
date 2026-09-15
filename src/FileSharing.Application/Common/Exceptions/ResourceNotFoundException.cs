using FileSharing.Application.Common.Errors;

namespace FileSharing.Application.Common.Exceptions;

/// <summary>Always 404. Overloaded per enum family — a generic ResourceErrorCode.NotFound for cases with no more specific code, or a domain-specific one (e.g. FileErrorCode.NotFound).</summary>
public sealed class ResourceNotFoundException : AppException
{
    public ResourceNotFoundException(ResourceErrorCode code, string message)
        : base(404, code, ErrorCodeCatalog.Map(code), message)
    {
    }

    public ResourceNotFoundException(FileErrorCode code, string message)
        : base(404, code, ErrorCodeCatalog.Map(code), message)
    {
    }
}
