using FileSharing.Application.Abstractions.Security;
using FileSharing.Application.Common.Errors;
using FileSharing.Application.Common.Exceptions;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Application.Observability;
using FileSharing.Application.Services.Auth;
using FileSharing.Domain.Entities;
using FileSharing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FileSharing.UnitTests.Application.Auth;

/// <summary>
/// Focused on the one thing this phase changed here — Register/Login/GetCurrentUser now throw
/// AppException instead of returning Result.Failure. The underlying business rules themselves
/// (duplicate email, wrong password...) are already covered end-to-end by
/// FileSharing.ApiTests.Auth.AuthEndpointsTests; this class exists so a mapping regression would
/// fail at the unit level too, not only at the HTTP level.
/// </summary>
public class AuthServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IJwtTokenGenerator> _jwtTokenGeneratorMock = new();
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _sut = new AuthService(_dbContext, _passwordHasherMock.Object, _jwtTokenGeneratorMock.Object, new AppMetrics(), NullLogger<AuthService>.Instance);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task RegisterAsync_WithDuplicateEmail_ThrowsConflictWithEmailAlreadyExists()
    {
        _dbContext.Users.Add(new User("user@example.com", "hash"));
        await _dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.RegisterAsync(new RegisterRequest("user@example.com", "SenhaForte123")));

        Assert.Equal(AuthErrorCode.EmailAlreadyExists, exception.Code);
        Assert.Equal(409, exception.HttpStatusCode);
    }

    [Fact]
    public async Task LoginAsync_WithUnknownEmail_ThrowsAuthenticationExceptionWithInvalidCredentials()
    {
        var exception = await Assert.ThrowsAsync<AuthenticationException>(() =>
            _sut.LoginAsync(new LoginRequest("nobody@example.com", "whatever")));

        Assert.Equal(AuthErrorCode.InvalidCredentials, exception.Code);
        Assert.Equal(401, exception.HttpStatusCode);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ThrowsAuthenticationExceptionWithInvalidCredentials()
    {
        _dbContext.Users.Add(new User("user@example.com", "correct-hash"));
        await _dbContext.SaveChangesAsync();
        _passwordHasherMock.Setup(h => h.VerifyPassword("correct-hash", "wrong-password")).Returns(false);

        var exception = await Assert.ThrowsAsync<AuthenticationException>(() =>
            _sut.LoginAsync(new LoginRequest("user@example.com", "wrong-password")));

        Assert.Equal(AuthErrorCode.InvalidCredentials, exception.Code);
    }

    [Fact]
    public async Task LoginAsync_WithCorrectCredentials_ReturnsAToken()
    {
        _dbContext.Users.Add(new User("user@example.com", "correct-hash"));
        await _dbContext.SaveChangesAsync();
        _passwordHasherMock.Setup(h => h.VerifyPassword("correct-hash", "correct-password")).Returns(true);
        _jwtTokenGeneratorMock
            .Setup(g => g.GenerateToken(It.IsAny<User>()))
            .Returns(("token-value", DateTimeOffset.UtcNow.AddHours(1)));

        var response = await _sut.LoginAsync(new LoginRequest("user@example.com", "correct-password"));

        Assert.Equal("token-value", response.AccessToken);
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithUnknownUserId_ThrowsAuthenticationException()
    {
        var exception = await Assert.ThrowsAsync<AuthenticationException>(() =>
            _sut.GetCurrentUserAsync(Guid.NewGuid()));

        Assert.Equal(AuthErrorCode.InvalidCredentials, exception.Code);
    }
}
