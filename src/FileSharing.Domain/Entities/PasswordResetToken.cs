namespace FileSharing.Domain.Entities;

/// <summary>
/// A single-use, short-lived password reset request. Deliberately its own entity, its own hash,
/// its own expiration — never the JWT, never File's AccessTokenHash mechanism. Only the SHA-256
/// hash of the plaintext token is ever persisted here (see Application.Common.AccessTokenHasher,
/// reused for this same "high-entropy token → deterministic lookup hash" purpose); the plaintext
/// value exists only for the instant it takes to email it, and is never stored anywhere.
/// </summary>
public class PasswordResetToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }

    public User User { get; private set; } = null!;

    private PasswordResetToken()
    {
    }

    public PasswordResetToken(Guid userId, string tokenHash, DateTimeOffset createdAtUtc, TimeSpan validFor)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId is required.", nameof(userId));

        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("TokenHash is required.", nameof(tokenHash));

        if (validFor <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(validFor), "validFor must be greater than zero.");

        Id = Guid.NewGuid();
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAtUtc;
        ExpiresAt = createdAtUtc.Add(validFor);
    }

    public bool IsUsed => UsedAt is not null;

    public bool IsExpired(DateTimeOffset asOfUtc) => asOfUtc >= ExpiresAt;

    /// <summary>True only when the token can still be redeemed — neither already used nor past its expiration.</summary>
    public bool IsValid(DateTimeOffset asOfUtc) => !IsUsed && !IsExpired(asOfUtc);

    /// <summary>
    /// Marks this token as consumed. Called exactly once, at the moment the password is actually
    /// changed — a token that is merely validated (GET reset-password/{token}) is never marked
    /// used by that check alone, only a successful POST reset-password redeems it.
    /// </summary>
    public void MarkAsUsed(DateTimeOffset asOfUtc)
    {
        if (IsUsed)
            throw new InvalidOperationException("This password reset token has already been used.");

        if (IsExpired(asOfUtc))
            throw new InvalidOperationException("Cannot use an expired password reset token.");

        UsedAt = asOfUtc;
    }

    /// <summary>
    /// Invalidates a still-active token without "using" it — called on every still-valid token
    /// belonging to a user when they request a fresh reset, so at most one reset link is ever
    /// live at a time (see PasswordResetService.ForgotPasswordAsync).
    /// </summary>
    public void Invalidate(DateTimeOffset asOfUtc)
    {
        if (IsUsed)
            return;

        UsedAt = asOfUtc;
    }
}
