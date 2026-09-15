namespace FileSharing.Application.Common.Errors;

/// <summary>
/// Authentication-domain errors — login, registration, password reset. Deliberately never
/// includes an authorization ("you're logged in but not allowed") concern; that lives in
/// <see cref="AuthorizationErrorCode"/> so 401 and 403 are never mixed by accident (see
/// ErrorCodeCatalog remarks). The enum's own member names are never sent to a client — see
/// ErrorCodeCatalog for the stable public strings — so renaming a member here is not a breaking
/// change for Mobile/Web.
/// </summary>
public enum AuthErrorCode
{
    InvalidCredentials,
    EmailAlreadyExists,
    InvalidEmail,
    AccountDisabled,
    PasswordResetInvalid,
    PasswordResetExpired,
    PasswordResetUsed,
    PasswordResetRateLimited
}
