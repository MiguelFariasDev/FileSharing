using FileSharing.Application.Abstractions.Email;
using FileSharing.Application.Abstractions.Security;
using FileSharing.Application.Common;
using FileSharing.Application.Common.Errors;
using FileSharing.Application.Common.Exceptions;
using FileSharing.Application.Observability;
using FileSharing.Application.Services.Auth;
using FileSharing.Domain.Entities;
using FileSharing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FileSharing.UnitTests.Application.Auth;

public class PasswordResetServiceTests : IDisposable
{
    private const string Email = "user@example.com";
    private const string OldPasswordHash = "old-hash";

    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IEmailService> _emailServiceMock = new();
    private readonly PasswordResetService _sut;

    public PasswordResetServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);

        _passwordHasherMock
            .Setup(h => h.HashPassword(It.IsAny<string>()))
            .Returns<string>(p => $"hashed:{p}");

        _sut = new PasswordResetService(
            _dbContext,
            _passwordHasherMock.Object,
            _emailServiceMock.Object,
            Options.Create(new PasswordResetOptions { TokenExpirationMinutes = 30, WebResetUrlBase = "https://app.test/reset-password" }),
            new AppMetrics(),
            NullLogger<PasswordResetService>.Instance);
    }

    public void Dispose() => _dbContext.Dispose();

    private async Task<User> SeedUserAsync(string email = Email)
    {
        var user = new User(email, OldPasswordHash);
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task ForgotPasswordAsync_WithKnownEmail_CreatesATokenAndSendsAnEmail()
    {
        await SeedUserAsync();

        await _sut.ForgotPasswordAsync(Email);

        var token = await _dbContext.PasswordResetTokens.SingleAsync();
        Assert.False(token.IsUsed);
        Assert.False(token.IsExpired(DateTimeOffset.UtcNow));

        _emailServiceMock.Verify(
            e => e.SendPasswordResetEmailAsync(Email, It.Is<string>(link => link.StartsWith("https://app.test/reset-password?token=")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ForgotPasswordAsync_NeverPersistsThePlaintextToken()
    {
        await SeedUserAsync();
        string? sentLink = null;
        _emailServiceMock
            .Setup(e => e.SendPasswordResetEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, link, _) => sentLink = link)
            .Returns(Task.CompletedTask);

        await _sut.ForgotPasswordAsync(Email);

        var plaintextToken = new Uri(sentLink!).Query.Split("token=")[1];
        var persistedToken = await _dbContext.PasswordResetTokens.SingleAsync();

        Assert.NotEqual(plaintextToken, persistedToken.TokenHash);
        Assert.Equal(64, persistedToken.TokenHash.Length); // SHA-256 hex digest — see AccessTokenHasher
    }

    [Fact]
    public async Task ForgotPasswordAsync_WithUnknownEmail_CreatesNoTokenAndSendsNoEmail()
    {
        await _sut.ForgotPasswordAsync("nobody@example.com");

        Assert.Empty(_dbContext.PasswordResetTokens);
        _emailServiceMock.Verify(
            e => e.SendPasswordResetEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ForgotPasswordAsync_CalledTwice_InvalidatesTheFirstToken()
    {
        await SeedUserAsync();

        await _sut.ForgotPasswordAsync(Email);
        var firstToken = await _dbContext.PasswordResetTokens.SingleAsync();

        await _sut.ForgotPasswordAsync(Email);

        var refreshedFirstToken = await _dbContext.PasswordResetTokens.SingleAsync(t => t.Id == firstToken.Id);
        Assert.True(refreshedFirstToken.IsUsed);
        Assert.Equal(2, await _dbContext.PasswordResetTokens.CountAsync());
    }

    [Fact]
    public async Task ValidateResetTokenAsync_WithUnknownToken_ThrowsInvalidWith404()
    {
        var exception = await Assert.ThrowsAsync<DomainException>(() => _sut.ValidateResetTokenAsync("unknown-token"));

        Assert.Equal(AuthErrorCode.PasswordResetInvalid, exception.Code);
        Assert.Equal(404, exception.HttpStatusCode);
    }

    [Fact]
    public async Task ValidateResetTokenAsync_WithExpiredToken_ThrowsExpiredWith410()
    {
        var user = await SeedUserAsync();
        var token = await SeedTokenAsync(user.Id, DateTimeOffset.UtcNow.AddHours(-2), TimeSpan.FromMinutes(30));

        var exception = await Assert.ThrowsAsync<DomainException>(() => _sut.ValidateResetTokenAsync(token));

        Assert.Equal(AuthErrorCode.PasswordResetExpired, exception.Code);
        Assert.Equal(410, exception.HttpStatusCode);
    }

    [Fact]
    public async Task ValidateResetTokenAsync_WithUsedToken_ThrowsUsedWith410()
    {
        var user = await SeedUserAsync();
        var token = await SeedTokenAsync(user.Id, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30), used: true);

        var exception = await Assert.ThrowsAsync<DomainException>(() => _sut.ValidateResetTokenAsync(token));

        Assert.Equal(AuthErrorCode.PasswordResetUsed, exception.Code);
        Assert.Equal(410, exception.HttpStatusCode);
    }

    [Fact]
    public async Task ValidateResetTokenAsync_WithValidToken_DoesNotThrow()
    {
        var user = await SeedUserAsync();
        var token = await SeedTokenAsync(user.Id, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));

        await _sut.ValidateResetTokenAsync(token);
    }

    [Fact]
    public async Task ResetPasswordAsync_WithValidToken_ChangesThePasswordAndMarksTheTokenUsed()
    {
        var user = await SeedUserAsync();
        var token = await SeedTokenAsync(user.Id, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));

        await _sut.ResetPasswordAsync(token, "NewPassword123");

        var refreshedUser = await _dbContext.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal("hashed:NewPassword123", refreshedUser.PasswordHash);
        Assert.NotEqual(OldPasswordHash, refreshedUser.PasswordHash);

        var refreshedToken = await _dbContext.PasswordResetTokens.SingleAsync(t => t.UserId == user.Id);
        Assert.True(refreshedToken.IsUsed);
    }

    [Fact]
    public async Task ResetPasswordAsync_WithAlreadyUsedToken_ThrowsAndDoesNotChangeThePassword()
    {
        var user = await SeedUserAsync();
        var token = await SeedTokenAsync(user.Id, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30), used: true);

        await Assert.ThrowsAsync<DomainException>(() => _sut.ResetPasswordAsync(token, "NewPassword123"));

        var refreshedUser = await _dbContext.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(OldPasswordHash, refreshedUser.PasswordHash);
    }

    private async Task<string> SeedTokenAsync(Guid userId, DateTimeOffset createdAt, TimeSpan validFor, bool used = false)
    {
        const string plaintextToken = "known-plaintext-token";
        var tokenHash = AccessTokenHasher.Hash(plaintextToken);
        var token = new PasswordResetToken(userId, tokenHash, createdAt, validFor);

        if (used)
            token.MarkAsUsed(createdAt.Add(validFor / 2));

        _dbContext.PasswordResetTokens.Add(token);
        await _dbContext.SaveChangesAsync();

        return plaintextToken;
    }
}
