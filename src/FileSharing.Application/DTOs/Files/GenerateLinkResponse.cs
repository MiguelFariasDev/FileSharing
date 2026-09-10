namespace FileSharing.Application.DTOs.Files;

/// <summary>
/// The plaintext <see cref="AccessToken"/> is produced here and only here — once returned to
/// the caller it is gone; only its hash (<c>File.AccessTokenHash</c>) is ever persisted.
/// </summary>
public record GenerateLinkResponse(Guid FileId, string AccessToken);
