using System.Diagnostics.Metrics;
using FileSharing.Application.Observability;

namespace FileSharing.UnitTests.Application.Observability;

/// <summary>
/// Each test builds its own Meter (via AppMetrics' internal test-only constructor overload) and
/// its own MeterListener subscribed only to that Meter — never the process-wide default Meter
/// AppMetrics()'s public constructor would otherwise register into — so these tests can run in
/// parallel without observing each other's counter increments.
/// </summary>
public class AppMetricsTests : IDisposable
{
    private readonly Meter _meter;
    private readonly MeterListener _listener;
    private readonly List<(string Instrument, long Value, KeyValuePair<string, object?>[] Tags)> _longMeasurements = [];
    private readonly List<(string Instrument, double Value, KeyValuePair<string, object?>[] Tags)> _doubleMeasurements = [];
    private readonly AppMetrics _sut;

    public AppMetricsTests()
    {
        _meter = new Meter($"FileSharing.Application.Tests.{Guid.NewGuid():N}");
        _sut = new AppMetrics(_meter);

        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter == _meter)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            _longMeasurements.Add((instrument.Name, measurement, tags.ToArray())));
        _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
            _doubleMeasurements.Add((instrument.Name, measurement, tags.ToArray())));
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
        _meter.Dispose();
    }

    [Fact]
    public void UploadInitiated_RecordsOneCountOnTheUploadsInitiatedCounter()
    {
        _sut.UploadInitiated();

        Assert.Contains(_longMeasurements, m => m.Instrument == "filesharing.uploads.initiated" && m.Value == 1);
    }

    [Fact]
    public void UploadCompleted_TagsTheResult_AndRecordsTheDuration()
    {
        _sut.UploadCompleted(success: true, durationMs: 42.5);

        var counted = Assert.Single(_longMeasurements, m => m.Instrument == "filesharing.uploads.completed");
        Assert.Contains(counted.Tags, t => t.Key == "result" && (string?)t.Value == "success");

        var timed = Assert.Single(_doubleMeasurements, m => m.Instrument == "filesharing.uploads.duration");
        Assert.Equal(42.5, timed.Value);
    }

    [Fact]
    public void UploadCompleted_Failure_TagsResultAsFailure()
    {
        _sut.UploadCompleted(success: false, durationMs: 5);

        var counted = Assert.Single(_longMeasurements, m => m.Instrument == "filesharing.uploads.completed");
        Assert.Contains(counted.Tags, t => t.Key == "result" && (string?)t.Value == "failure");
    }

    [Fact]
    public void Download_TagsResult_AndRecordsDuration()
    {
        _sut.Download(success: true, durationMs: 10);

        Assert.Contains(_longMeasurements, m => m.Instrument == "filesharing.downloads");
        Assert.Contains(_doubleMeasurements, m => m.Instrument == "filesharing.downloads.duration" && m.Value == 10);
    }

    [Fact]
    public void AuthAttempt_TagsBothOperationAndResult()
    {
        _sut.AuthAttempt("login", success: false);

        var counted = Assert.Single(_longMeasurements, m => m.Instrument == "filesharing.auth.attempts");
        Assert.Contains(counted.Tags, t => t.Key == "operation" && (string?)t.Value == "login");
        Assert.Contains(counted.Tags, t => t.Key == "result" && (string?)t.Value == "failure");
    }

    [Fact]
    public void FilesExpired_WithZeroCount_RecordsNothing()
    {
        // Avoids a meaningless zero-valued measurement cluttering the series every single time
        // a cleanup run finds nothing to expire (the overwhelmingly common case).
        _sut.FilesExpired(0);

        Assert.DoesNotContain(_longMeasurements, m => m.Instrument == "filesharing.files.expired");
    }

    [Fact]
    public void FilesExpired_WithAPositiveCount_RecordsThatCount()
    {
        _sut.FilesExpired(3);

        Assert.Contains(_longMeasurements, m => m.Instrument == "filesharing.files.expired" && m.Value == 3);
    }

    [Fact]
    public void NoMeasurement_EverCarriesAUserIdOrFileIdOrTokenTag()
    {
        _sut.UploadInitiated();
        _sut.UploadCompleted(true, 1);
        _sut.Download(true, 1);
        _sut.PublicLinkAccessed(true);
        _sut.LinkGenerated();
        _sut.AuthAttempt("login", true);
        _sut.FilesExpired(1);
        _sut.StorageObjectDeleted();
        _sut.CleanupFailure();
        _sut.CleanupCompleted(1);
        _sut.SignalRNotification(true);

        var disallowedTagNames = new[] { "userid", "fileid", "token", "ip", "ipaddress" };

        foreach (var (_, _, tags) in _longMeasurements)
            Assert.DoesNotContain(tags, t => disallowedTagNames.Contains(t.Key.ToLowerInvariant()));

        foreach (var (_, _, tags) in _doubleMeasurements)
            Assert.DoesNotContain(tags, t => disallowedTagNames.Contains(t.Key.ToLowerInvariant()));
    }
}
