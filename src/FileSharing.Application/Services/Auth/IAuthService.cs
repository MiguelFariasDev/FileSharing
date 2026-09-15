using FileSharing.Application.DTOs.Auth;

namespace FileSharing.Application.Services.Auth;

/// <summary>
/// Every method here either succeeds or throws one of the FileSharing.Application.Common.Exceptions
/// types (AuthenticationException/ConflictException/DomainException) — never a Result-of-failure —
/// so GlobalExceptionHandler is the single place an auth failure becomes an HTTP response.
/// </summary>
public interface IAuthService
{
    Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<UserResponse> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
