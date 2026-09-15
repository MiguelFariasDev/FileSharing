using FileSharing.Domain.Entities;

namespace FileSharing.UnitTests.Domain.Entities;

public class PasswordResetTokenTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static PasswordResetToken CreateToken(DateTimeOffset? createdAt = null, TimeSpan? validFor = null) =>
        new(UserId, "token-hash", createdAt ?? DateTimeOffset.UtcNow, validFor ?? TimeSpan.FromMinutes(30));

    [Fact]
    public void Constructor_SetsExpiresAt_ToCreatedAtPlusValidFor()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var token = CreateToken(createdAt, TimeSpan.FromMinutes(30));

        Assert.Equal(createdAt.AddMinutes(30), token.ExpiresAt);
    }

    [Fact]
    public void Constructor_StartsUnused()
    {
        var token = CreateToken();

        Assert.False(token.IsUsed);
        Assert.Null(token.UsedAt);
    }

    [Fact]
    public void Constructor_Throws_WhenUserIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            new PasswordResetToken(Guid.Empty, "token-hash", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void Constructor_Throws_WhenTokenHashIsEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            new PasswordResetToken(UserId, "   ", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void Constructor_Throws_WhenValidForIsNotPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PasswordResetToken(UserId, "token-hash", DateTimeOffset.UtcNow, TimeSpan.Zero));
    }

    [Fact]
    public void IsExpired_ReturnsFalse_BeforeExpiresAt()
    {
        var token = CreateToken();

        Assert.False(token.IsExpired(token.ExpiresAt.AddSeconds(-1)));
    }

    [Fact]
    public void IsExpired_ReturnsTrue_AtOrAfterExpiresAt()
    {
        var token = CreateToken();

        Assert.True(token.IsExpired(token.ExpiresAt));
        Assert.True(token.IsExpired(token.ExpiresAt.AddSeconds(1)));
    }

    [Fact]
    public void IsValid_ReturnsTrue_WhenNeitherUsedNorExpired()
    {
        var token = CreateToken();

        Assert.True(token.IsValid(token.CreatedAt));
    }

    [Fact]
    public void IsValid_ReturnsFalse_WhenExpired()
    {
        var token = CreateToken();

        Assert.False(token.IsValid(token.ExpiresAt));
    }

    [Fact]
    public void IsValid_ReturnsFalse_WhenUsed()
    {
        var token = CreateToken();
        token.MarkAsUsed(token.CreatedAt);

        Assert.False(token.IsValid(token.CreatedAt));
    }

    [Fact]
    public void MarkAsUsed_SetsUsedAt()
    {
        var token = CreateToken();
        var usedAt = token.CreatedAt.AddMinutes(1);

        token.MarkAsUsed(usedAt);

        Assert.True(token.IsUsed);
        Assert.Equal(usedAt, token.UsedAt);
    }

    [Fact]
    public void MarkAsUsed_Throws_WhenAlreadyUsed()
    {
        var token = CreateToken();
        token.MarkAsUsed(token.CreatedAt);

        Assert.Throws<InvalidOperationException>(() => token.MarkAsUsed(token.CreatedAt.AddMinutes(1)));
    }

    [Fact]
    public void MarkAsUsed_Throws_WhenExpired()
    {
        var token = CreateToken();

        Assert.Throws<InvalidOperationException>(() => token.MarkAsUsed(token.ExpiresAt));
    }

    [Fact]
    public void Invalidate_SetsUsedAt_WhenNotAlreadyUsed()
    {
        var token = CreateToken();
        var asOf = token.CreatedAt.AddMinutes(1);

        token.Invalidate(asOf);

        Assert.True(token.IsUsed);
        Assert.Equal(asOf, token.UsedAt);
    }

    [Fact]
    public void Invalidate_IsANoOp_WhenAlreadyUsed()
    {
        var token = CreateToken();
        token.MarkAsUsed(token.CreatedAt);
        var originalUsedAt = token.UsedAt;

        token.Invalidate(token.CreatedAt.AddDays(1));

        Assert.Equal(originalUsedAt, token.UsedAt);
    }
}
