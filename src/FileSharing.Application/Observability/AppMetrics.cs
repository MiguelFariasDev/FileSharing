using System.Diagnostics.Metrics;

namespace FileSharing.Application.Observability;

/// <summary>
/// Thin wrapper around <see cref="System.Diagnostics.Metrics.Meter"/> — part of the .NET base
/// class library since .NET 6, not a third-party dependency — for the handful of counters/
/// histograms this phase asks for. Registered as a singleton and injected into the
/// Application/Infrastructure services that own each operation, the same way <c>ILogger&lt;T&gt;</c>
/// already is.
///
/// Deliberately not wired to any exporter (see docs/security.md's Observability section for why
/// OpenTelemetry's SDK/exporter packages were left out of this phase): every counter/histogram
/// created here is visible today via <c>dotnet-counters monitor --process-id &lt;pid&gt; FileSharing.Application</c>
/// with zero extra configuration, and is already in exactly the shape an OpenTelemetry
/// MeterProvider would consume later via a single <c>.AddMeter(AppMetrics.MeterName)</c> call —
/// this class does not need to change when that happens.
///
/// Tags are deliberately limited to a few stable, low-cardinality values (operation/result) —
/// never a UserId, FileId, token, or IP — so no metric here can explode into unbounded time
/// series or leak an identifier through a metrics backend.
/// </summary>
public sealed class AppMetrics
{
    public const string MeterName = "FileSharing.Application";

    private readonly Counter<long> _uploadsInitiated;
    private readonly Counter<long> _uploadsCompleted;
    private readonly Counter<long> _downloads;
    private readonly Counter<long> _publicLinkAccesses;
    private readonly Counter<long> _linksGenerated;
    private readonly Counter<long> _authAttempts;
    private readonly Counter<long> _filesExpired;
    private readonly Counter<long> _storageObjectsDeleted;
    private readonly Counter<long> _cleanupFailures;
    private readonly Counter<long> _signalRNotifications;
    private readonly Histogram<double> _uploadDurationMs;
    private readonly Histogram<double> _downloadDurationMs;
    private readonly Histogram<double> _cleanupDurationMs;

    public AppMetrics() : this(new Meter(MeterName))
    {
    }

    // Internal ctor seam for tests: a test-owned Meter can be attached to its own
    // MeterListener without racing other tests that also construct an AppMetrics against the
    // shared, process-wide default Meter registry.
    internal AppMetrics(Meter meter)
    {
        _uploadsInitiated = meter.CreateCounter<long>("filesharing.uploads.initiated");
        _uploadsCompleted = meter.CreateCounter<long>("filesharing.uploads.completed");
        _downloads = meter.CreateCounter<long>("filesharing.downloads");
        _publicLinkAccesses = meter.CreateCounter<long>("filesharing.public_links.accessed");
        _linksGenerated = meter.CreateCounter<long>("filesharing.public_links.generated");
        _authAttempts = meter.CreateCounter<long>("filesharing.auth.attempts");
        _filesExpired = meter.CreateCounter<long>("filesharing.files.expired");
        _storageObjectsDeleted = meter.CreateCounter<long>("filesharing.storage.objects_deleted");
        _cleanupFailures = meter.CreateCounter<long>("filesharing.cleanup.failures");
        _signalRNotifications = meter.CreateCounter<long>("filesharing.signalr.notifications");
        _uploadDurationMs = meter.CreateHistogram<double>("filesharing.uploads.duration", unit: "ms");
        _downloadDurationMs = meter.CreateHistogram<double>("filesharing.downloads.duration", unit: "ms");
        _cleanupDurationMs = meter.CreateHistogram<double>("filesharing.cleanup.duration", unit: "ms");
    }

    public void UploadInitiated() => _uploadsInitiated.Add(1);

    public void UploadCompleted(bool success, double durationMs)
    {
        _uploadsCompleted.Add(1, new KeyValuePair<string, object?>("result", Result(success)));
        _uploadDurationMs.Record(durationMs);
    }

    public void Download(bool success, double durationMs)
    {
        _downloads.Add(1, new KeyValuePair<string, object?>("result", Result(success)));
        _downloadDurationMs.Record(durationMs);
    }

    public void PublicLinkAccessed(bool success) =>
        _publicLinkAccesses.Add(1, new KeyValuePair<string, object?>("result", Result(success)));

    public void LinkGenerated() => _linksGenerated.Add(1);

    public void AuthAttempt(string operation, bool success) =>
        _authAttempts.Add(
            1,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("result", Result(success)));

    public void FilesExpired(int count)
    {
        if (count > 0)
            _filesExpired.Add(count);
    }

    public void StorageObjectDeleted() => _storageObjectsDeleted.Add(1);

    public void CleanupFailure() => _cleanupFailures.Add(1);

    public void CleanupCompleted(double durationMs) => _cleanupDurationMs.Record(durationMs);

    public void SignalRNotification(bool success) =>
        _signalRNotifications.Add(1, new KeyValuePair<string, object?>("result", Result(success)));

    private static string Result(bool success) => success ? "success" : "failure";
}
