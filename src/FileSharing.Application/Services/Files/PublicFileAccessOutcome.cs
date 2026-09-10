using FileSharing.Application.DTOs.Files;

namespace FileSharing.Application.Services.Files;

/// <summary>
/// Deliberately has no failure reason: an unknown token, an expired file, and a file that no
/// longer exists must all be indistinguishable to the caller. Whatever the cause, the only
/// signal exposed here is "not available".
/// </summary>
public class PublicFileAccessOutcome
{
    public bool IsSuccess { get; }
    public PublicFileAccessResponse? Value { get; }

    private PublicFileAccessOutcome(bool isSuccess, PublicFileAccessResponse? value)
    {
        IsSuccess = isSuccess;
        Value = value;
    }

    public static PublicFileAccessOutcome Success(PublicFileAccessResponse value) => new(true, value);

    public static PublicFileAccessOutcome NotAvailable() => new(false, null);
}
