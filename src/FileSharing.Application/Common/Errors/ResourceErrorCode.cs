namespace FileSharing.Application.Common.Errors;

/// <summary>Generic, domain-agnostic resource errors — used where a more specific enum (e.g. FileErrorCode) does not apply.</summary>
public enum ResourceErrorCode
{
    NotFound,
    Conflict
}
