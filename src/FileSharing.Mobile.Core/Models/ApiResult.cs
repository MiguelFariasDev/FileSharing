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

    protected ApiResult(bool isSuccess, ApiErrorType? errorType, string? message)
    {
        IsSuccess = isSuccess;
        ErrorType = errorType;
        Message = message;
    }

    public static ApiResult Success() => new(true, null, null);
    public static ApiResult Failure(ApiErrorType errorType, string message) => new(false, errorType, message);
}

public class ApiResult<T> : ApiResult
{
    public T? Value { get; }

    private ApiResult(bool isSuccess, T? value, ApiErrorType? errorType, string? message)
        : base(isSuccess, errorType, message)
    {
        Value = value;
    }

    public static ApiResult<T> Success(T value) => new(true, value, null, null);
    public static new ApiResult<T> Failure(ApiErrorType errorType, string message) => new(false, default, errorType, message);
}
