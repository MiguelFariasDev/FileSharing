using System.Diagnostics;
using FileSharing.Application.Abstractions.Notifications;
using FileSharing.Application.Abstractions.Persistence;
using FileSharing.Application.Abstractions.Storage;
using FileSharing.Application.Common;
using FileSharing.Application.Common.Errors;
using FileSharing.Application.Common.Exceptions;
using FileSharing.Application.DTOs.Files;
using FileSharing.Application.DTOs.Notifications;
using FileSharing.Application.Observability;
using FileSharing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FileSharing.Application.Services.Files;

public class FileDownloadService : IFileDownloadService
{
    /// <summary>
    /// Deliberately the one message used for every reason a public download can fail — unknown
    /// token, expired file, wrong status, missing storage object — so the response is identical
    /// regardless of cause (see PublicFilesController remarks on anti-enumeration).
    /// </summary>
    private const string DownloadNotAvailableError = "Arquivo não disponível.";

    private readonly IApplicationDbContext _dbContext;
    private readonly IFileStorageService _fileStorageService;
    private readonly IFileDownloadNotifier _fileDownloadNotifier;
    private readonly AppMetrics _metrics;
    private readonly ILogger<FileDownloadService> _logger;

    public FileDownloadService(
        IApplicationDbContext dbContext,
        IFileStorageService fileStorageService,
        IFileDownloadNotifier fileDownloadNotifier,
        AppMetrics metrics,
        ILogger<FileDownloadService> logger)
    {
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
        _fileDownloadNotifier = fileDownloadNotifier;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<DownloadUrlResponse> DownloadAsync(
        string accessToken,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var accessTokenHash = AccessTokenHasher.Hash(accessToken);

        var file = await _dbContext.Files
            .SingleOrDefaultAsync(f => f.AccessTokenHash == accessTokenHash, cancellationToken);

        // Same generic outcome for every reason a download cannot proceed — unknown token,
        // wrong status (still PendingUpload, or Expired), time-based expiry checked directly
        // against ExpiresAt (never relying on a cleanup job to have flipped Status), and a
        // missing storage object. None of these are distinguishable from one another below.
        // Never logged: the token/hash, the caller's IP/User-Agent (see docs/security.md).
        if (file is null || !file.IsActive || file.IsExpired())
        {
            _logger.LogInformation("Public download denied: token unavailable or file not active.");
            _metrics.Download(success: false, stopwatch.Elapsed.TotalMilliseconds);
            throw new ResourceNotFoundException(FileErrorCode.NotFound, DownloadNotAvailableError);
        }

        var objectExists = await _fileStorageService.ObjectExistsAsync(file.StorageKey, cancellationToken);
        if (!objectExists)
        {
            _logger.LogWarning("Public download denied: object missing from storage. FileId={FileId}", file.Id);
            _metrics.Download(success: false, stopwatch.Elapsed.TotalMilliseconds);
            throw new ResourceNotFoundException(FileErrorCode.NotFound, DownloadNotAvailableError);
        }

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

        // Best-effort, after everything the downloader needs has already succeeded: a
        // SignalR outage (or any other notifier failure) must never turn an already-persisted
        // Download + already-issued presigned URL into a failed response. The notifier
        // implementation is expected to handle/log its own failures (see
        // IFileDownloadNotifier/SignalRFileDownloadNotifier), but this catch is a deliberate
        // second safety net in case a future/alternate implementation doesn't honor that — this
        // exact guarantee is the single most important requirement of this integration.
        try
        {
            await _fileDownloadNotifier.NotifyDownloadAsync(
                file.UserId,
                new FileDownloadedNotification(file.Id, file.OriginalFileName, download.DownloadedAt),
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Reachable only if a notifier implementation fails without handling its own
            // errors — SignalRFileDownloadNotifier already logs (and records a metric for) its
            // own failures, so this is a defensive last resort, not the primary log line for a
            // SignalR failure.
            _logger.LogWarning(ex, "Download notifier threw unexpectedly. FileId={FileId}", file.Id);
        }

        _logger.LogInformation("Public download completed. FileId={FileId} UserId={UserId}", file.Id, file.UserId);
        _metrics.Download(success: true, stopwatch.Elapsed.TotalMilliseconds);

        return new DownloadUrlResponse(presigned.Url, presigned.ExpiresAt);
    }
}
