using FileSharing.Application.Abstractions.Persistence;
using FileSharing.Application.Abstractions.Storage;
using FileSharing.Application.Common;
using FileSharing.Application.DTOs.Files;
using FileSharing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FileSharing.Application.Services.Files;

public class FileDownloadService : IFileDownloadService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IFileStorageService _fileStorageService;

    public FileDownloadService(IApplicationDbContext dbContext, IFileStorageService fileStorageService)
    {
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
    }

    public async Task<DownloadFileOutcome> DownloadAsync(
        string accessToken,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        var accessTokenHash = AccessTokenHasher.Hash(accessToken);

        var file = await _dbContext.Files
            .SingleOrDefaultAsync(f => f.AccessTokenHash == accessTokenHash, cancellationToken);

        // Same generic outcome for every reason a download cannot proceed — unknown token,
        // wrong status (still PendingUpload, or Expired), time-based expiry checked directly
        // against ExpiresAt (never relying on a cleanup job to have flipped Status), and a
        // missing storage object. None of these are distinguishable from one another below.
        if (file is null || !file.IsActive || file.IsExpired())
            return DownloadFileOutcome.NotAvailable();

        var objectExists = await _fileStorageService.ObjectExistsAsync(file.StorageKey, cancellationToken);
        if (!objectExists)
            return DownloadFileOutcome.NotAvailable();

        // "Download" is recorded here as "an authorized presigned URL was issued to the
        // caller" — not "the client finished transferring the bytes". Once the URL leaves
        // this process, the API has no way to observe whether the S3 GET actually happened
        // (or completed, or was resumed, or abandoned), so "issued" is the only event it can
        // truthfully claim. The record is only written once every check above has already
        // passed, so a Download row always corresponds to a request that really was allowed
        // to proceed — never to a rejected/expired/missing-object attempt.
        var download = new Download(file.Id, ipAddress, userAgent);
        _dbContext.Downloads.Add(download);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Generated after persisting the Download: this is a local HMAC computation with no
        // network call (see S3FileStorageService), so it cannot itself fail — persisting the
        // record first guarantees a successful response is never returned without one.
        var presigned = await _fileStorageService.CreatePresignedDownloadUrlAsync(file.StorageKey, cancellationToken);

        return DownloadFileOutcome.Success(new DownloadUrlResponse(presigned.Url, presigned.ExpiresAt));
    }
}
