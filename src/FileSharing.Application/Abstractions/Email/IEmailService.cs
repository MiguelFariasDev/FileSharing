namespace FileSharing.Application.Abstractions.Email;

/// <summary>
/// The only place Application asks for an email to be sent — Application never depends on
/// SMTP/AWS SES/SendGrid/MailKit directly (Infrastructure implements this; see
/// docs/security.md). Deliberately one narrow, purpose-specific method rather than a generic
/// "send any email" API: this project has exactly one email today (password reset), and a
/// generic abstraction with no second caller would just be speculative surface area.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// <paramref name="resetLink"/> already contains the plaintext token in its query string —
    /// this is the only place that plaintext value is allowed to exist outside the moment it was
    /// generated; it is never persisted (only its hash is, in PasswordResetToken.TokenHash) and
    /// never logged by the caller (see PasswordResetService).
    /// </summary>
    Task SendPasswordResetEmailAsync(string toEmail, string resetLink, CancellationToken cancellationToken = default);
}
