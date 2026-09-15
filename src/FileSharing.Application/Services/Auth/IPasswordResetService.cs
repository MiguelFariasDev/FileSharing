namespace FileSharing.Application.Services.Auth;

/// <summary>
/// Every method here either succeeds (returns) or throws — see IAuthService's own remarks.
/// <see cref="ForgotPasswordAsync"/> is the one exception to "throws on failure": an unknown
/// email is not a failure the caller may observe (see its own remarks) — the same
/// Task-returning shape is kept regardless, so a controller never has to branch on
/// "did the account exist" either.
/// </summary>
public interface IPasswordResetService
{
    Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Throws a DomainException(AuthErrorCode.PasswordReset{Invalid,Expired,Used}) if the token
    /// cannot currently be redeemed; returns normally (nothing to report) when it can.
    /// </summary>
    Task ValidateResetTokenAsync(string token, CancellationToken cancellationToken = default);

    Task ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default);
}
