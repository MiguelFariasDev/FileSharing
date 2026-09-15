namespace FileSharing.Web.Tests.Services;

/// <summary>
/// Like FakeHttpMessageHandler, but can answer differently per request — needed for component
/// tests that exercise a flow spanning more than one Api call (e.g. login, then GET /api/auth/me).
/// </summary>
public class RoutedFakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;

    public List<HttpRequestMessage> Requests { get; } = [];

    public RoutedFakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : this(request => Task.FromResult(responder(request)))
    {
    }

    /// <summary>
    /// Async overload — lets a test control exactly when a response completes (e.g. via a
    /// TaskCompletionSource) to observe a component's in-flight/loading state, instead of every
    /// response resolving synchronously before the test can inspect anything.
    /// </summary>
    public RoutedFakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return _responder(request);
    }
}
