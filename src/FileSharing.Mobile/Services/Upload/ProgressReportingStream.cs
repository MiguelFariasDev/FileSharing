namespace FileSharing.Mobile.Services.Upload;

/// <summary>
/// Wraps a read-only stream to report upload progress as it's consumed by HttpClient —
/// HttpClient has no built-in upload-progress callback, so this is the standard way to
/// surface it to an IProgress&lt;double&gt;.
/// </summary>
internal sealed class ProgressReportingStream : Stream
{
    private readonly Stream _inner;
    private readonly IProgress<double>? _progress;
    private readonly long _totalLength;
    private long _bytesRead;

    public ProgressReportingStream(Stream inner, IProgress<double>? progress)
    {
        _inner = inner;
        _progress = progress;
        _totalLength = inner.Length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _totalLength;

    public override long Position
    {
        get => _bytesRead;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        ReportProgress(read);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await _inner.ReadAsync(buffer, offset, count, cancellationToken);
        ReportProgress(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken);
        ReportProgress(read);
        return read;
    }

    private void ReportProgress(int bytesJustRead)
    {
        if (bytesJustRead <= 0)
            return;

        _bytesRead += bytesJustRead;

        if (_totalLength > 0)
            _progress?.Report(Math.Min(1.0, _bytesRead / (double)_totalLength));
    }

    public override void Flush() => _inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();

        base.Dispose(disposing);
    }
}
