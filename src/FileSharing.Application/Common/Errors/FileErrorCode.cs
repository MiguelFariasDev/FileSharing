namespace FileSharing.Application.Common.Errors;

/// <summary>File-domain errors — upload, ownership, public links. See AuthErrorCode's remarks on why enum member names are never the public contract.</summary>
public enum FileErrorCode
{
    NotFound,
    Expired,
    AccessDenied,
    InvalidType,
    InvalidUploadState,
    InvalidFileName,
    UploadNotCompleted
}
