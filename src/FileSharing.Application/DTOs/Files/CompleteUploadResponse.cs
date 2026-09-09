namespace FileSharing.Application.DTOs.Files;

public record CompleteUploadResponse(
    Guid FileId,
    string OriginalFileName,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);
