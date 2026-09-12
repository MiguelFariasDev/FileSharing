namespace FileSharing.Infrastructure.BackgroundJobs;

public class ExpirationCleanupOptions
{
    public const string SectionName = "ExpirationCleanup";

    /// <summary>
    /// When false, the API never registers Hangfire storage/server nor the recurring
    /// job — <see cref="IExpiredFileCleanupJob"/> is still resolvable via DI (so it can
    /// still be invoked directly, e.g. from a test), it just never runs on a schedule.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Approximate recurring interval, in minutes, used to build the Hangfire cron
    /// expression ("*/{IntervalMinutes} * * * *"). This is eventual cleanup, not a
    /// security boundary — see <see cref="IExpiredFileCleanupJob"/> remarks.
    /// </summary>
    public int IntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Maximum number of expired candidates processed per execution, so one run never
    /// loads an unbounded number of rows into memory. A backlog larger than this drains
    /// over multiple recurring executions rather than all at once.
    /// </summary>
    public int BatchSize { get; set; } = 100;
}
