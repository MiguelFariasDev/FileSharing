namespace FileSharing.Application.DTOs.Auth;

public record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt);
