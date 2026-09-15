namespace FileSharing.Application.Common.Errors;

/// <summary>
/// The one place an internal <c>*ErrorCode</c> enum value is turned into the stable, public
/// string a client (Mobile/Web, and any future integration) actually sees and is allowed to
/// branch on. Deliberately explicit per value (never <c>errorCode.ToString().ToUpperInvariant()</c>)
/// so renaming an enum member here never silently changes the public contract — see each
/// exception type's own remarks for why the enum's internal name and the public code are kept
/// independent. A simple static class rather than an <c>IErrorCodeMapper</c> abstraction: there is
/// nothing here to substitute in a test or swap at runtime, so an interface would only add an
/// indirection with no real seam behind it.
/// </summary>
public static class ErrorCodeCatalog
{
    public static string Map(AuthErrorCode code) => code switch
    {
        AuthErrorCode.InvalidCredentials => "AUTH_INVALID_CREDENTIALS",
        AuthErrorCode.EmailAlreadyExists => "AUTH_EMAIL_ALREADY_EXISTS",
        AuthErrorCode.InvalidEmail => "AUTH_EMAIL_INVALID",
        AuthErrorCode.AccountDisabled => "AUTH_ACCOUNT_DISABLED",
        AuthErrorCode.PasswordResetInvalid => "AUTH_PASSWORD_RESET_INVALID",
        AuthErrorCode.PasswordResetExpired => "AUTH_PASSWORD_RESET_EXPIRED",
        AuthErrorCode.PasswordResetUsed => "AUTH_PASSWORD_RESET_USED",
        AuthErrorCode.PasswordResetRateLimited => "AUTH_PASSWORD_RESET_RATE_LIMITED",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Missing public code mapping for AuthErrorCode.")
    };

    public static string Map(FileErrorCode code) => code switch
    {
        FileErrorCode.NotFound => "FILE_NOT_FOUND",
        FileErrorCode.Expired => "FILE_EXPIRED",
        FileErrorCode.AccessDenied => "FILE_ACCESS_DENIED",
        FileErrorCode.InvalidType => "FILE_INVALID_TYPE",
        FileErrorCode.InvalidUploadState => "FILE_UPLOAD_INVALID_STATE",
        FileErrorCode.InvalidFileName => "FILE_INVALID_NAME",
        FileErrorCode.UploadNotCompleted => "FILE_UPLOAD_NOT_COMPLETED",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Missing public code mapping for FileErrorCode.")
    };

    public static string Map(AuthorizationErrorCode code) => code switch
    {
        AuthorizationErrorCode.Forbidden => "AUTH_FORBIDDEN",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Missing public code mapping for AuthorizationErrorCode.")
    };

    public static string Map(ValidationErrorCode code) => code switch
    {
        ValidationErrorCode.InvalidRequest => "VALIDATION_ERROR",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Missing public code mapping for ValidationErrorCode.")
    };

    public static string Map(ResourceErrorCode code) => code switch
    {
        ResourceErrorCode.NotFound => "RESOURCE_NOT_FOUND",
        ResourceErrorCode.Conflict => "RESOURCE_CONFLICT",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Missing public code mapping for ResourceErrorCode.")
    };

    public static string Map(SystemErrorCode code) => code switch
    {
        SystemErrorCode.UnexpectedError => "INTERNAL_ERROR",
        SystemErrorCode.ServiceUnavailable => "SERVICE_UNAVAILABLE",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Missing public code mapping for SystemErrorCode.")
    };

    public static string Map(RateLimitErrorCode code) => code switch
    {
        RateLimitErrorCode.TooManyRequests => "RATE_LIMITED",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Missing public code mapping for RateLimitErrorCode.")
    };
}
