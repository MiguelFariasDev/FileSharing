using Hangfire;

namespace FileSharing.Infrastructure.BackgroundJobs;

/// <summary>
/// Recurring cleanup for files whose <c>ExpiresAt</c> has passed: deletes the S3 object and
/// marks the <c>File</c> row <c>Expired</c>. This job exists purely for physical/logical
/// consistency (freeing storage, keeping <c>Status</c> accurate) — it is never consulted for
/// authorization. The public download/link endpoints already reject on
/// <c>ExpiresAt &lt;= DateTimeOffset.UtcNow</c> independently of <c>Status</c>, so a file stays
/// inaccessible immediately at expiry even if this job is stopped, delayed, or failing (see
/// docs/architecture.md).
///
/// The Hangfire attributes below are declared on this interface method (not on the
/// implementing class) because Hangfire's job filter pipeline reads attributes off the
/// <see cref="System.Reflection.MethodInfo"/> captured by the scheduling expression
/// (<c>RecurringJob.AddOrUpdate&lt;IExpiredFileCleanupJob&gt;(...)</c>), which points at the
/// interface method — attributes placed on the concrete override would never be seen by
/// Hangfire. They are inert when the class is invoked directly, as unit/integration tests do,
/// which is intentional: per CLAUDE.md, this job's `Execute`-equivalent is tested as a plain
/// class, never through Hangfire's own scheduler.
///
/// <see cref="DisableConcurrentExecutionAttribute"/>: one instance of this job runs at a time
/// across all app instances, enforced by Hangfire's own distributed lock (backed by the same
/// PostgreSQL storage) rather than a custom in-process/global lock. The timeout only bounds how
/// long a caller waits to acquire the lock if a previous run is still in flight; it does not
/// cap the job's own run time.
///
/// <see cref="AutomaticRetryAttribute"/>: a single problematic file never throws out of the
/// processing loop (see implementation), so a whole-job exception here means something systemic
/// (e.g. the database or the batch query itself is unavailable) — worth a few retries, but not
/// Hangfire's default of 10. If retries are exhausted the job simply fails visibly; the next
/// ~15-minute recurring run picks the same candidates back up regardless.
/// </summary>
public interface IExpiredFileCleanupJob
{
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 3)]
    Task<ExpiredFileCleanupResult> ExecuteAsync(CancellationToken cancellationToken = default);
}
