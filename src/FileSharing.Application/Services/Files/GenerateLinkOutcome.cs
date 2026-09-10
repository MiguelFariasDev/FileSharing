using FileSharing.Application.DTOs.Files;

namespace FileSharing.Application.Services.Files;

public enum GenerateLinkFailureReason
{
    /// <summary>File does not exist, or exists but belongs to a different user.</summary>
    NotFound,

    /// <summary>File exists and is owned by the caller, but its current state does not allow a link (not yet completed, or expired).</summary>
    Conflict
}

public class GenerateLinkOutcome
{
    public bool IsSuccess { get; }
    public GenerateLinkResponse? Value { get; }
    public GenerateLinkFailureReason? FailureReason { get; }
    public string? Error { get; }

    private GenerateLinkOutcome(bool isSuccess, GenerateLinkResponse? value, GenerateLinkFailureReason? failureReason, string? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        FailureReason = failureReason;
        Error = error;
    }

    public static GenerateLinkOutcome Success(GenerateLinkResponse value) => new(true, value, null, null);

    public static GenerateLinkOutcome Failure(GenerateLinkFailureReason reason, string error) => new(false, null, reason, error);
}
