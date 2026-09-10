namespace FileSharing.Application.DTOs.Files;

/// <summary>
/// Minimal information a holder of a valid, unexpired public link is allowed to see.
/// Deliberately excludes <c>StorageKey</c>/<c>UserId</c> — the actual download flow
/// (presigned GET URL) belongs to a later phase.
/// </summary>
public record PublicFileAccessResponse(
    Guid FileId,
    string OriginalFileName,
    long SizeBytes,
    string ContentType,
    DateTimeOffset ExpiresAt);
