using FileSharing.Application.DTOs.Files;

namespace FileSharing.Mobile.Core.Services.Upload;

public enum UploadFailureReason
{
    /// <summary>initiate rejected the request — invalid type/size/name (Fase 3's own validation).</summary>
    ValidationFailed,

    /// <summary>Could not reach the API at all (initiate or complete) — offline, DNS, timeout.</summary>
    Network,

    /// <summary>The direct PUT to S3 itself failed — expired/invalid presigned URL, a real S3-side
    /// error, or a network failure specifically during the transfer.</summary>
    StorageUploadFailed,

    /// <summary>complete rejected the request (404/409) — object missing, size/Content-Type
    /// mismatch, or the upload was not pending anymore.</summary>
    CompleteFailed,

    Cancelled,
    Unknown
}

public class UploadOutcome
{
    public bool IsSuccess { get; }
    public CompleteUploadResponse? Result { get; }
    public UploadFailureReason? FailureReason { get; }
    public string? UserMessage { get; }

    private UploadOutcome(bool isSuccess, CompleteUploadResponse? result, UploadFailureReason? failureReason, string? userMessage)
    {
        IsSuccess = isSuccess;
        Result = result;
        FailureReason = failureReason;
        UserMessage = userMessage;
    }

    public static UploadOutcome Success(CompleteUploadResponse result) => new(true, result, null, null);

    public static UploadOutcome Failure(UploadFailureReason reason, string userMessage) => new(false, null, reason, userMessage);
}
