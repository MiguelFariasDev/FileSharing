namespace FileSharing.Application.DTOs.Files;

/// <summary>
/// A short-lived, GET-only presigned URL for the actual object — the client downloads
/// directly from storage; the file's bytes never flow through this API.
/// </summary>
public record DownloadUrlResponse(string DownloadUrl, DateTimeOffset ExpiresAt);
