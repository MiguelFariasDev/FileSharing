using FileSharing.Application.Abstractions.Persistence;
using FileSharing.Application.Abstractions.Security;
using FileSharing.Application.Common.Errors;
using FileSharing.Application.Common.Exceptions;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Application.Observability;
using FileSharing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FileSharing.Application.Services.Auth;

public class AuthService : IAuthService
{
    private const string InvalidCredentialsMessage = "Credenciais inválidas.";
    private const string EmailAlreadyRegisteredMessage = "Não foi possível concluir o cadastro.";

    private readonly IApplicationDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly AppMetrics _metrics;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IApplicationDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        AppMetrics metrics,
        ILogger<AuthService> logger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);

        var emailExists = await _dbContext.Users
            .AnyAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (emailExists)
        {
            // Never log the email itself — a log line is also a place an attacker could use to
            // enumerate registered addresses if they ever gained log access, same principle as
            // the API response already collapsing this into a single generic error.
            _logger.LogWarning("Registration rejected: email already registered.");
            _metrics.AuthAttempt("register", success: false);
            throw new ConflictException(AuthErrorCode.EmailAlreadyExists, EmailAlreadyRegisteredMessage);
        }

        var passwordHash = _passwordHasher.HashPassword(request.Password);
        var user = new User(normalizedEmail, passwordHash);

        _dbContext.Users.Add(user);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _logger.LogWarning("Registration rejected: email already registered (race with a concurrent request).");
            _metrics.AuthAttempt("register", success: false);
            throw new ConflictException(AuthErrorCode.EmailAlreadyExists, EmailAlreadyRegisteredMessage);
        }

        _logger.LogInformation("User registered. UserId={UserId}", user.Id);
        _metrics.AuthAttempt("register", success: true);
        return new UserResponse(user.Id, user.Email);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);

        var user = await _dbContext.Users
            .SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user is null || !_passwordHasher.VerifyPassword(user.PasswordHash, request.Password))
        {
            // Deliberately no email, no indication of which of the two reasons applied — same
            // discipline as the generic "Credenciais inválidas." response this backs.
            _logger.LogWarning("Login rejected: invalid credentials.");
            _metrics.AuthAttempt("login", success: false);
            throw new AuthenticationException(AuthErrorCode.InvalidCredentials, InvalidCredentialsMessage);
        }

        var (accessToken, expiresAt) = _jwtTokenGenerator.GenerateToken(user);
        _logger.LogInformation("Login succeeded. UserId={UserId}", user.Id);
        _metrics.AuthAttempt("login", success: true);
        return new AuthResponse(accessToken, expiresAt);
    }

    public async Task<UserResponse> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
            throw new AuthenticationException(AuthErrorCode.InvalidCredentials, InvalidCredentialsMessage);

        return new UserResponse(user.Id, user.Email);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
