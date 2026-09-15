using FileSharing.Application.Abstractions.Persistence;
using FileSharing.Application.Common.Errors;
using FileSharing.Application.Common.Exceptions;
using FileSharing.Application.DTOs.Files;
using Microsoft.EntityFrameworkCore;

namespace FileSharing.Application.Services.Files;

public class FileQueryService : IFileQueryService
{
    private const string FileNotFoundError = "Arquivo não encontrado.";

    private readonly IApplicationDbContext _dbContext;

    public FileQueryService(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<FileSummaryResponse>> GetMyFilesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Files
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FileSummaryResponse(
                f.Id,
                f.OriginalFileName,
                f.ContentType,
                f.SizeBytes,
                f.IsFolder,
                f.Status.ToString(),
                f.CreatedAt,
                f.ExpiresAt,
                f.Downloads.Count,
                f.AccessTokenHash != null))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DownloadHistoryEntryResponse>> GetDownloadHistoryAsync(
        Guid userId,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var file = await _dbContext.Files.SingleOrDefaultAsync(f => f.Id == fileId, cancellationToken);

        // Same generic error for "does not exist" and "belongs to someone else" — never reveal
        // to a caller whether a given file id belongs to another user (same pattern as
        // FileUploadService.CompleteUploadAsync).
        if (file is null || file.UserId != userId)
            throw new ResourceNotFoundException(FileErrorCode.NotFound, FileNotFoundError);

        return await _dbContext.Downloads
            .Where(d => d.FileId == fileId)
            .OrderByDescending(d => d.DownloadedAt)
            .Select(d => new DownloadHistoryEntryResponse(d.DownloadedAt))
            .ToListAsync(cancellationToken);
    }
}
