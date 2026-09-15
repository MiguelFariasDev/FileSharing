namespace FileSharing.Application.Abstractions.Security;

public class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    /// <summary>How long a requested reset token stays redeemable before ExpiresAt.</summary>
    public int TokenExpirationMinutes { get; set; } = 30;

    /// <summary>
    /// Base URL of the Web reset-password page (no trailing slash), e.g.
    /// "https://app.example.com/reset-password" — the emailed link is this plus
    /// "?token={plaintext token}". The Mobile app does not need this value; it has its own
    /// reset-password screen where the token is entered directly (see docs/mobile.md).
    /// </summary>
    public string WebResetUrlBase { get; set; } = string.Empty;
}
