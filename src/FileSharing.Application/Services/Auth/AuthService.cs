using FileSharing.Application.Abstractions.Persistence;
using FileSharing.Application.Abstractions.Security;
using FileSharing.Application.Common;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FileSharing.Application.Services.Auth;

public class AuthService : IAuthService
{
    private const string InvalidCredentialsError = "Credenciais inválidas.";
    private const string EmailAlreadyRegisteredError = "Não foi possível concluir o cadastro.";

    private readonly IApplicationDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public AuthService(
        IApplicationDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<Result<UserResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);

        var emailExists = await _dbContext.Users
            .AnyAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (emailExists)
            return Result<UserResponse>.Failure(EmailAlreadyRegisteredError);

        var passwordHash = _passwordHasher.HashPassword(request.Password);
        var user = new User(normalizedEmail, passwordHash);

        _dbContext.Users.Add(user);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result<UserResponse>.Failure(EmailAlreadyRegisteredError);
        }

        return Result<UserResponse>.Success(new UserResponse(user.Id, user.Email));
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);

        var user = await _dbContext.Users
            .SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user is null || !_passwordHasher.VerifyPassword(user.PasswordHash, request.Password))
            return Result<AuthResponse>.Failure(InvalidCredentialsError);

        var (accessToken, expiresAt) = _jwtTokenGenerator.GenerateToken(user);
        return Result<AuthResponse>.Success(new AuthResponse(accessToken, expiresAt));
    }

    public async Task<Result<UserResponse>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
            return Result<UserResponse>.Failure(InvalidCredentialsError);

        return Result<UserResponse>.Success(new UserResponse(user.Id, user.Email));
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
