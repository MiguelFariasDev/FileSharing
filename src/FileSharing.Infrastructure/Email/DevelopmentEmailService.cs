using FileSharing.Application.Abstractions.Email;

namespace FileSharing.Infrastructure.Email;

/// <summary>
/// The only IEmailService implementation today (see docs/security.md) — no real provider
/// (SMTP/AWS SES/SendGrid) is wired up yet, on purpose, per this phase's scope. Writes straight
/// to the console (Console.WriteLine), never through ILogger/Serilog: the reset link contains
/// the plaintext token, and that value must never enter this project's structured logging
/// pipeline (the same one that could ship to CloudWatch/a log aggregator in a real deployment) —
/// see PasswordResetService, which never logs it either. Console output is a local-developer-only
/// convenience that exists specifically so this flow can be exercised without a real mailbox;
/// swapping in a real provider later (e.g. an SesEmailService) needs no change anywhere else,
/// since callers only ever depend on IEmailService.
/// </summary>
public class DevelopmentEmailService : IEmailService
{
    public Task SendPasswordResetEmailAsync(string toEmail, string resetLink, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("=== DEV EMAIL (no real message sent — see DevelopmentEmailService) ===");
        Console.WriteLine($"To: {toEmail}");
        Console.WriteLine("Subject: Redefinição de senha — FileSharing");
        Console.WriteLine($"Link: {resetLink}");
        Console.WriteLine("======================================================================");

        return Task.CompletedTask;
    }
}
