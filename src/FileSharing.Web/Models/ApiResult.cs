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

    public static ApiResult Success() => new() { IsSuccess = true };
    public static ApiResult Failure(ApiErrorType errorType, string message) => new() { IsSuccess = false, ErrorType = errorType, Message = message };
}

public class ApiResult<T>
{
    public bool IsSuccess { get; private init; }
    public T? Value { get; private init; }
    public ApiErrorType? ErrorType { get; private init; }
    public string? Message { get; private init; }

    public static ApiResult<T> Success(T value) => new() { IsSuccess = true, Value = value };
    public static ApiResult<T> Failure(ApiErrorType errorType, string message) => new() { IsSuccess = false, ErrorType = errorType, Message = message };
}
