using FileSharing.Application.DTOs.Files;

namespace FileSharing.Application.Services.Files;

/// <summary>
/// Deliberately has no failure reason — same rationale as <see cref="PublicFileAccessOutcome"/>:
/// an unknown token, an expired file, a file in any non-Active status, and a file whose object
/// is missing from storage must all be indistinguishable to the caller.
/// </summary>
public class DownloadFileOutcome
{
    public bool IsSuccess { get; }
    public DownloadUrlResponse? Value { get; }

    private DownloadFileOutcome(bool isSuccess, DownloadUrlResponse? value)
    {
        IsSuccess = isSuccess;
        Value = value;
    }

    public static DownloadFileOutcome Success(DownloadUrlResponse value) => new(true, value);

    public static DownloadFileOutcome NotAvailable() => new(false, null);
}
