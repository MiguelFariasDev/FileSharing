using System.Diagnostics;
using FileSharing.Application.Abstractions.Persistence;
using FileSharing.Application.Abstractions.Storage;
using FileSharing.Application.Observability;
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
    private readonly AppMetrics _metrics;
    private readonly ILogger<ExpiredFileCleanupJob> _logger;

    public ExpiredFileCleanupJob(
        IApplicationDbContext dbContext,
        IFileStorageService fileStorageService,
        IOptions<ExpirationCleanupOptions> options,
        AppMetrics metrics,
        ILogger<ExpiredFileCleanupJob> logger)
    {
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
        _options = options.Value;
        _metrics = metrics;
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

                // Etapa 16 (medido, não especulativo — ver docs/performance.md, seção Hangfire):
                // sem desanexar, o change tracker do EF Core acumula uma entidade rastreada por
                // arquivo já processado neste laço, e SaveChangesAsync roda DetectChanges() sobre
                // TODAS elas a cada chamada — custo por item crescendo com o tamanho do lote
                // (quadrático no total). Medido: ~4-5ms/arquivo em lotes de 100 rodados em
                // sequência, contra ~18ms/arquivo processando 600 de uma vez sem isto.
                //
                // Importante: só o `file` já salvo é desanexado — nunca ChangeTracker.Clear(),
                // que desanexaria TODOS os candidatos ainda tracked, inclusive os que este mesmo
                // laço ainda vai processar (`candidates` inteiro veio de uma única ToListAsync()
                // antes do laço começar); um candidato futuro desanexado prematuramente nunca
                // teria sua própria mutação (`MarkAsExpired()`) persistida por
                // SaveChangesAsync, que só considera entidades tracked — bug real encontrado e
                // corrigido durante a implementação desta otimização, coberto por
                // ExpiredFileCleanupJobTests (UnitTests) já existentes.
                _dbContext.Entry(file).State = EntityState.Detached;

                expired++;
                _metrics.StorageObjectDeleted();
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
                _metrics.CleanupFailure();
            }
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "Expired file cleanup finished in {ElapsedMilliseconds}ms: {Expired} expired, {Failed} failed, {CandidateCount} candidate(s) considered.",
            stopwatch.ElapsedMilliseconds, expired, failed, candidates.Count);

        _metrics.FilesExpired(expired);
        _metrics.CleanupCompleted(stopwatch.Elapsed.TotalMilliseconds);

        return new ExpiredFileCleanupResult(candidates.Count, expired, failed);
    }
}
