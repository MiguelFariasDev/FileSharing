namespace FileSharing.Web.Models;

/// <summary>
/// Result of a call with no meaningful return value (e.g. register). See <see cref="ApiResult{T}"/>
/// for the value-returning counterpart — kept as a separate type rather than a generic
/// specialization to avoid an awkward Unit/object placeholder type.
/// </summary>
public class ApiResult
{
    public bool IsSuccess { get; private init; }
    public ApiErrorType? ErrorType { get; private init; }

    /// <summary>User-facing message only — never a raw exception message, stack trace, SQL, or
    /// any other implementation detail. See FileSharingApiClient for how this is derived.</summary>
    public string? Message { get; private init; }

    /// <summary>
    /// The Api's stable public error code (e.g. "AUTH_PASSWORD_RESET_EXPIRED") when the response
    /// body carried one — null for a network-level failure with no body at all. Callers that need
    /// to distinguish between failures sharing the same HTTP status (several password-reset
    /// failures are both 410 Gone, for instance) should branch on this, never on
    /// <see cref="Message"/> — see docs/api-errors.md.
    /// </summary>
    public string? Code { get; private init; }

    public static ApiResult Success() => new() { IsSuccess = true };
    public static ApiResult Failure(ApiErrorType errorType, string message, string? code = null) => new() { IsSuccess = false, ErrorType = errorType, Message = message, Code = code };
}

public class ApiResult<T>
{
    public bool IsSuccess { get; private init; }
    public T? Value { get; private init; }
    public ApiErrorType? ErrorType { get; private init; }
    public string? Message { get; private init; }
    public string? Code { get; private init; }

    public static ApiResult<T> Success(T value) => new() { IsSuccess = true, Value = value };
    public static ApiResult<T> Failure(ApiErrorType errorType, string message, string? code = null) => new() { IsSuccess = false, ErrorType = errorType, Message = message, Code = code };
}
