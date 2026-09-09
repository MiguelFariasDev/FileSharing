using FileSharing.Domain.Entities;

namespace FileSharing.Application.Abstractions.Security;

public interface IJwtTokenGenerator
{
    (string AccessToken, DateTimeOffset ExpiresAt) GenerateToken(User user);
}
