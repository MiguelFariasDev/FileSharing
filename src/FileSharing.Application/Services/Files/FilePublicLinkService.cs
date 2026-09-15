using FileSharing.Application.Abstractions.Persistence;
using FileSharing.Application.Common;
using FileSharing.Application.Common.Errors;
using FileSharing.Application.Common.Exceptions;
using FileSharing.Application.DTOs.Files;
using FileSharing.Application.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FileSharing.Application.Services.Files;

public class FilePublicLinkService : IFilePublicLinkService
{
    private const string FileNotFoundError = "Arquivo não encontrado.";
    private const string FileNotReadyError = "O upload ainda não foi concluído.";
    private const string FileExpiredError = "O arquivo expirou.";

    /// <summary>
    /// Deliberately the one message used for every reason a public token lookup can fail —
    /// unknown token, expired file, removed file — so the response is identical regardless of
    /// cause (see PublicFilesController remarks on anti-enumeration).
    /// </summary>
    private const string PublicFileNotAvailableError = "Arquivo não disponível.";

    private readonly IApplicationDbContext _dbContext;
    private readonly AppMetrics _metrics;
    private readonly ILogger<FilePublicLinkService> _logger;

    public FilePublicLinkService(IApplicationDbContext dbContext, AppMetrics metrics, ILogger<FilePublicLinkService> logger)
    {
        _dbContext = dbContext;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<GenerateLinkResponse> GenerateLinkAsync(
        Guid userId,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var file = await _dbContext.Files.SingleOrDefaultAsync(f => f.Id == fileId, cancellationToken);

        // Same generic error for "does not exist" and "belongs to someone else" — never
        // reveal to a caller whether a given file id belongs to another user.
        if (file is null || file.UserId != userId)
        {
            _logger.LogWarning("Generate link rejected: file not found or not owned by caller. FileId={FileId} UserId={UserId}", fileId, userId);
            throw new ResourceNotFoundException(FileErrorCode.NotFound, FileNotFoundError);
        }

        if (!file.IsActive)
        {
            _logger.LogWarning("Generate link rejected: file not active. FileId={FileId} Status={Status}", file.Id, file.Status);
            throw new ConflictException(FileErrorCode.UploadNotCompleted, FileNotReadyError);
        }

        var now = DateTimeOffset.UtcNow;
        if (file.IsExpired(now))
        {
            _logger.LogWarning("Generate link rejected: file already expired. FileId={FileId}", file.Id);
            throw new ConflictException(FileErrorCode.Expired, FileExpiredError);
        }

        // A fresh token every call — regenerating invalidates whatever link was issued before,
        // since only the hash is kept and the old plaintext can never be recovered anyway.
        // Never logged: neither the plaintext token nor its hash, here or anywhere else.
        var accessToken = RandomTokenGenerator.Generate();
        file.AssignAccessToken(AccessTokenHasher.Hash(accessToken), now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Public link generated. FileId={FileId} UserId={UserId}", file.Id, userId);
        _metrics.LinkGenerated();

        return new GenerateLinkResponse(file.Id, accessToken);
    }

    public async Task<PublicFileAccessResponse> GetByAccessTokenAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var accessTokenHash = AccessTokenHasher.Hash(accessToken);

        var file = await _dbContext.Files
            .SingleOrDefaultAsync(f => f.AccessTokenHash == accessTokenHash, cancellationToken);

        if (file is null || !file.IsActive || file.IsExpired())
        {
            // Deliberately never logs the token or its hash, and never a FileId when file is
            // null (there is nothing legitimate to correlate an unknown-token attempt to).
            _logger.LogInformation("Public file access denied: token unavailable or file not active.");
            _metrics.PublicLinkAccessed(success: false);
            throw new ResourceNotFoundException(FileErrorCode.NotFound, PublicFileNotAvailableError);
        }

        _logger.LogInformation("Public file accessed. FileId={FileId}", file.Id);
        _metrics.PublicLinkAccessed(success: true);

        return new PublicFileAccessResponse(
            file.Id,
            file.OriginalFileName,
            file.SizeBytes,
            file.ContentType,
            file.ExpiresAt!.Value);
    }
}
