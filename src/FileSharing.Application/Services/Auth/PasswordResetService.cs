using FileSharing.Application.Abstractions.Email;
using FileSharing.Application.Abstractions.Persistence;
using FileSharing.Application.Abstractions.Security;
using FileSharing.Application.Common;
using FileSharing.Application.Common.Errors;
using FileSharing.Application.Common.Exceptions;
using FileSharing.Application.Observability;
using FileSharing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FileSharing.Application.Services.Auth;

public class PasswordResetService : IPasswordResetService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IEmailService _emailService;
    private readonly PasswordResetOptions _options;
    private readonly AppMetrics _metrics;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(
        IApplicationDbContext dbContext,
        IPasswordHasher passwordHasher,
        IEmailService emailService,
        IOptions<PasswordResetOptions> options,
        AppMetrics metrics,
        ILogger<PasswordResetService> logger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _emailService = emailService;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>
    /// Never throws for "no such account" — an unknown email must be indistinguishable, to the
    /// caller, from a known one that just received an email (see docs/security.md's
    /// anti-enumeration section and Api.IntegrationTests' dedicated enumeration test).
    /// AuthController always answers this with the same 202 regardless of what happens in here.
    /// </summary>
    public async Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user is null)
        {
            // Deliberately no email in this log line — see AuthService's own remarks on the
            // same principle for login/register.
            _logger.LogInformation("Password reset requested for an unknown email.");
            _metrics.AuthAttempt("password-reset-request", success: false);
            return;
        }

        // At most one active reset link per user at a time — a previous, still-valid token
        // (from an earlier request the user never completed) stops working the moment a new
        // one is issued, rather than leaving multiple simultaneously-valid tokens around.
        var activeTokens = await _dbContext.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedAt == null)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        foreach (var activeToken in activeTokens)
            activeToken.Invalidate(now);

        var plaintextToken = RandomTokenGenerator.Generate();
        var tokenHash = AccessTokenHasher.Hash(plaintextToken);
        var resetToken = new PasswordResetToken(user.Id, tokenHash, now, TimeSpan.FromMinutes(_options.TokenExpirationMinutes));

        _dbContext.PasswordResetTokens.Add(resetToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var resetLink = $"{_options.WebResetUrlBase}?token={Uri.EscapeDataString(plaintextToken)}";

        try
        {
            await _emailService.SendPasswordResetEmailAsync(user.Email, resetLink, cancellationToken);
        }
        catch (Exception ex)
        {
            // A transient email-provider outage must not turn into a different-looking response
            // than the "unknown email" branch above — both end in the same 202. The token still
            // exists and is still valid; only the notification failed.
            _logger.LogWarning(ex, "Failed to send the password reset email. UserId={UserId}", user.Id);
        }

        _logger.LogInformation("Password reset requested. UserId={UserId}", user.Id);
        _metrics.AuthAttempt("password-reset-request", success: true);
    }

    public async Task ValidateResetTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        await FindValidTokenOrThrowAsync(token, cancellationToken);
    }

    public async Task ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        var resetToken = await FindValidTokenOrThrowAsync(token, cancellationToken);

        var user = await _dbContext.Users
            .SingleAsync(u => u.Id == resetToken.UserId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        user.ChangePasswordHash(_passwordHasher.HashPassword(newPassword));
        resetToken.MarkAsUsed(now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Password reset completed. UserId={UserId}", user.Id);
        _metrics.AuthAttempt("password-reset-complete", success: true);
    }

    /// <summary>
    /// The one place token lookup + state validation happens — both the "just checking"
    /// (ValidateResetTokenAsync) and "actually resetting" (ResetPasswordAsync) paths must reject
    /// exactly the same tokens for exactly the same reasons.
    /// </summary>
    private async Task<PasswordResetToken> FindValidTokenOrThrowAsync(string token, CancellationToken cancellationToken)
    {
        var tokenHash = AccessTokenHasher.Hash(token);

        var resetToken = await _dbContext.PasswordResetTokens
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (resetToken is null)
            throw new DomainException(AuthErrorCode.PasswordResetInvalid, "Este link de recuperação é inválido.", httpStatusCode: 404);

        var now = DateTimeOffset.UtcNow;

        if (resetToken.IsUsed)
            throw new DomainException(AuthErrorCode.PasswordResetUsed, "Este link de recuperação já foi utilizado.", httpStatusCode: 410);

        if (resetToken.IsExpired(now))
            throw new DomainException(AuthErrorCode.PasswordResetExpired, "Este link de recuperação expirou. Solicite uma nova recuperação de senha.", httpStatusCode: 410);

        return resetToken;
    }
}
