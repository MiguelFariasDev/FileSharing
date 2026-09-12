using System.Diagnostics;
using FileSharing.Application.Abstractions.Persistence;
using FileSharing.Application.Abstractions.Storage;
using FileSharing.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FileSharing.Infrastructure.BackgroundJobs;

public class ExpiredFileCleanupJob : IExpiredFileCleanupJob
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IFileStorageService _fileStorageService;
    private readonly ExpirationCleanupOptions _options;
    private readonly ILogger<ExpiredFileCleanupJob> _logger;

    public ExpiredFileCleanupJob(
        IApplicationDbContext dbContext,
        IFileStorageService fileStorageService,
        IOptions<ExpirationCleanupOptions> options,
        ILogger<ExpiredFileCleanupJob> logger)
    {
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ExpiredFileCleanupResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var utcNow = DateTimeOffset.UtcNow;

        // Status == Active is the primary filter — a PendingUpload file has no ExpiresAt
        // (null) and an already-Expired one is simply not a candidate again, so neither
        // needs special-casing here. Ordered oldest-expiry-first and capped at BatchSize so a
        // single execution never loads an unbounded number of rows into memory; a backlog
        // bigger than one batch drains across subsequent ~15-minute runs instead.
        var candidates = await _dbContext.Files
            .Where(f => f.Status == FileStatus.Active && f.ExpiresAt <= utcNow)
            .OrderBy(f => f.ExpiresAt)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "Expired file cleanup: {CandidateCount} candidate(s) found (batch size {BatchSize}).",
            candidates.Count, _options.BatchSize);

        var expired = 0;
        var failed = 0;

        foreach (var file in candidates)
        {
            try
            {
                // S3's DeleteObject is idempotent for a key that is already gone — AWS (and
                // LocalStack, matching it) return success rather than a not-found error when
                // deleting a nonexistent key. That means "object still there" and "object was
                // already removed by a previous partial run" both land here as success, with
                // no separate existence check needed for idempotency.
                await _fileStorageService.DeleteObjectAsync(file.StorageKey, cancellationToken);

                // Throws (InvalidOperationException) if Status is somehow not Active by now —
                // a domain-level guard against the query above ever being wrong, kept as
                // defense in depth rather than relied upon.
                file.MarkAsExpired();

                // Saved per file, immediately after its own storage delete already succeeded —
                // never inside a transaction that spans the S3 call (S3 does not participate
                // in a PostgreSQL transaction, so a long-lived transaction around it would only
                // widen the inconsistency window for no benefit). If this save throws, the S3
                // object is already gone but the row stays Active/un-expired; the next run
                // re-deletes (a no-op success, per the idempotency note above) and marks it
                // Expired then — a self-healing, documented inconsistency window rather than a
                // permanent one.
                await _dbContext.SaveChangesAsync(cancellationToken);

                expired++;
            }
            catch (Exception ex)
            {
                // Never marked Expired when the delete (or the subsequent save) failed — an
                // object must not be silently orphaned as "gone" in our records while a real
                // failure (as opposed to "already absent", handled above) may mean it is still
                // in the bucket. Logged with FileId only for correlation — never the original
                // file name, any token, or a presigned URL. One file's failure does not affect
                // the rest of the batch; it simply remains a candidate for the next run.
                failed++;
                _logger.LogError(ex, "Failed to expire file {FileId}; it remains a candidate for a future run.", file.Id);
            }
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "Expired file cleanup finished in {ElapsedMilliseconds}ms: {Expired} expired, {Failed} failed, {CandidateCount} candidate(s) considered.",
            stopwatch.ElapsedMilliseconds, expired, failed, candidates.Count);

        return new ExpiredFileCleanupResult(candidates.Count, expired, failed);
    }
}
