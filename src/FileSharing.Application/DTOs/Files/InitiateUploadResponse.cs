namespace FileSharing.Application.DTOs.Files;

public record InitiateUploadResponse(Guid FileId, string UploadUrl, DateTimeOffset ExpiresAt);
