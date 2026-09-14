namespace FileSharing.Application.DTOs.Files;

/// <summary>
/// One row of the authenticated user's own file list (GET /api/files/mine). Deliberately never
/// includes AccessTokenHash or the plaintext access token — <see cref="HasPublicLink"/> is only
/// a boolean so the client can offer "gerar link"/"gerar novo link" without ever seeing the
/// hash; the actual token is only ever obtained (plaintext, once) via POST /api/files/{id}/link.
/// </summary>
public record FileSummaryResponse(
    Guid FileId,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    bool IsFolder,
    string Status,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt,
    int DownloadCount,
    bool HasPublicLink);
