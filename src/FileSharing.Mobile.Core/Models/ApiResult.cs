namespace FileSharing.Mobile.Core.Models;

/// <summary>
/// Same shape/intent as FileSharing.Web's own ApiResult/ApiErrorType (Etapa 8) — kept as an
/// independent copy here rather than a shared reference, since Mobile has no business
/// referencing the Web project (a different, unrelated client of the same API) and there is no
/// FileSharing.Shared project in this solution to hold it instead. Every FileSharingApiClient
/// call returns one of these so ViewModels never need to catch HttpRequestException/parse
/// status codes themselves.
/// </summary>
public enum ApiErrorType
{
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    /// <summary>410 — a resource that existed but is no longer usable (an expired/already-used password-reset token). Distinguish which one via ApiResult.Code, not this enum.</summary>
    Gone,
    TooManyRequests,
    ValidationFailed,
    Network,
    ServerError
}

public class ApiResult
{
    public bool IsSuccess { get; }
    public ApiErrorType? ErrorType { get; }
    public string? Message { get; }

    /// <summary>
    /// The Api's stable public error code (e.g. "AUTH_PASSWORD_RESET_EXPIRED") when the response
    /// body carried one — null for a network-level failure with no body at all. Branch on this,
    /// never on <see cref="Message"/>, whenever failures can share an HTTP status — see
    /// docs/api-errors.md.
    /// </summary>
    public string? Code { get; }

    protected ApiResult(bool isSuccess, ApiErrorType? errorType, string? message, string? code)
    {
        IsSuccess = isSuccess;
        ErrorType = errorType;
        Message = message;
        Code = code;
    }

    public static ApiResult Success() => new(true, null, null, null);
    public static ApiResult Failure(ApiErrorType errorType, string message, string? code = null) => new(false, errorType, message, code);
}

public class ApiResult<T> : ApiResult
{
    public T? Value { get; }

    private ApiResult(bool isSuccess, T? value, ApiErrorType? errorType, string? message, string? code)
        : base(isSuccess, errorType, message, code)
    {
        Value = value;
    }

    public static ApiResult<T> Success(T value) => new(true, value, null, null, null);
    public static new ApiResult<T> Failure(ApiErrorType errorType, string message, string? code = null) => new(false, default, errorType, message, code);
}
