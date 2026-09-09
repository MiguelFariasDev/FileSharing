using FileSharing.Application.DTOs.Files;

namespace FileSharing.Application.Services.Files;

public enum CompleteUploadFailureReason
{
    /// <summary>File does not exist, or exists but belongs to a different user.</summary>
    NotFound,

    /// <summary>File exists and is owned by the caller, but its current state does not allow completion.</summary>
    Conflict
}

public class CompleteUploadOutcome
{
    public bool IsSuccess { get; }
    public CompleteUploadResponse? Value { get; }
    public CompleteUploadFailureReason? FailureReason { get; }
    public string? Error { get; }

    private CompleteUploadOutcome(bool isSuccess, CompleteUploadResponse? value, CompleteUploadFailureReason? failureReason, string? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        FailureReason = failureReason;
        Error = error;
    }

    public static CompleteUploadOutcome Success(CompleteUploadResponse value) => new(true, value, null, null);

    public static CompleteUploadOutcome Failure(CompleteUploadFailureReason reason, string error) => new(false, null, reason, error);
}
