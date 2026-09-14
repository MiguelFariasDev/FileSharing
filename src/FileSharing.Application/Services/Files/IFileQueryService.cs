using FileSharing.Application.Common;
using FileSharing.Application.DTOs.Files;

namespace FileSharing.Application.Services.Files;

/// <summary>
/// Read-only queries over the authenticated user's own files — backs the Etapa 8 dashboard
/// (GET /api/files/mine, GET /api/files/{id}/downloads). Ownership is always determined by the
/// caller-supplied <c>userId</c> (from the JWT, never from client input), never by trusting a
/// bare <c>fileId</c> alone.
/// </summary>
public interface IFileQueryService
{
    Task<IReadOnlyList<FileSummaryResponse>> GetMyFilesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same generic "not found" outcome whether the file does not exist or belongs to a
    /// different user — mirrors the ownership-check pattern already used by
    /// <see cref="IFileUploadService.CompleteUploadAsync"/> and <see cref="IFilePublicLinkService.GenerateLinkAsync"/>.
    /// </summary>
    Task<Result<IReadOnlyList<DownloadHistoryEntryResponse>>> GetDownloadHistoryAsync(
        Guid userId,
        Guid fileId,
        CancellationToken cancellationToken = default);
}
