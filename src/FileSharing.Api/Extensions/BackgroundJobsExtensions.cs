using FileSharing.Infrastructure.BackgroundJobs;
using Hangfire;
using Hangfire.PostgreSql;

namespace FileSharing.Api.Extensions;

/// <summary>
/// Wires Hangfire using the application's own PostgreSQL database as job storage — no Redis,
/// no second database. Deliberately never calls <c>app.UseHangfireDashboard()</c>: exposing
/// <c>/hangfire</c> without an authorization filter is out of scope for this phase (see
/// docs/security.md); Hangfire here is purely background-processing infrastructure.
/// </summary>
public static class BackgroundJobsExtensions
{
    private const string ExpiredFileCleanupJobId = "expired-file-cleanup";

    public static IServiceCollection AddBackgroundJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExpirationCleanupOptions>(configuration.GetSection(ExpirationCleanupOptions.SectionName));

        // Registered unconditionally: the job itself has no Hangfire dependency in its
        // constructor and can be resolved/invoked directly (as tests do) even when the
        // recurring schedule below is skipped.
        services.AddScoped<IExpiredFileCleanupJob, ExpiredFileCleanupJob>();

        if (!TryGetHangfireConnectionString(configuration, out var connectionString))
            return services;

        // Hangfire.PostgreSql creates/migrates its own "hangfire" schema on first use in the
        // same database — it never touches ApplicationDbContext's own tables/migrations.
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(connectionString));

        services.AddHangfireServer();

        return services;
    }

    /// <summary>
    /// Registers (or updates) the recurring cleanup job under the stable id
    /// <c>ExpiredFileCleanupJobId</c>, so a restart never duplicates it — Hangfire's
    /// <c>AddOrUpdate</c> is itself idempotent on that id. No-ops when Hangfire storage was not
    /// configured above (disabled, or no PostgreSQL connection string available — e.g. under
    /// the ApiTests' "Testing" environment, which uses an in-memory database and never wires a
    /// real Postgres connection at all).
    /// </summary>
    public static IApplicationBuilder UseExpiredFileCleanupSchedule(this WebApplication app)
    {
        if (!TryGetHangfireConnectionString(app.Configuration, out _))
            return app;

        var options = app.Configuration.GetSection(ExpirationCleanupOptions.SectionName).Get<ExpirationCleanupOptions>()
            ?? new ExpirationCleanupOptions();

        // Resolved from DI rather than the static RecurringJob facade: AddHangfire registers
        // the configured storage into the container (and into IRecurringJobManager), but does
        // not reliably set the static JobStorage.Current — using the static facade here throws
        // "Current JobStorage instance has not been initialized yet" even though storage was
        // configured correctly above. This is Hangfire's own documented recommendation for
        // ASP.NET Core apps (service-based APIs over static ones).
        var recurringJobManager = app.Services.GetRequiredService<IRecurringJobManager>();

        // Cron granularity only — "approximately every 15 minutes", never depending on
        // second-level precision, and tolerant of the process being briefly unavailable
        // exactly on a tick (Hangfire simply runs it on the next poll).
        recurringJobManager.AddOrUpdate<IExpiredFileCleanupJob>(
            ExpiredFileCleanupJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            $"*/{options.IntervalMinutes} * * * *");

        return app;
    }

    private static bool TryGetHangfireConnectionString(IConfiguration configuration, out string connectionString)
    {
        var options = configuration.GetSection(ExpirationCleanupOptions.SectionName).Get<ExpirationCleanupOptions>()
            ?? new ExpirationCleanupOptions();

        connectionString = configuration.GetConnectionString("Postgres") ?? string.Empty;

        return options.Enabled && !string.IsNullOrWhiteSpace(connectionString);
    }
}
