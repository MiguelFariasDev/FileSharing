namespace FileSharing.Web.Tests.Services;

/// <summary>
/// Like FakeHttpMessageHandler, but can answer differently per request — needed for component
/// tests that exercise a flow spanning more than one Api call (e.g. login, then GET /api/auth/me).
/// </summary>
public class RoutedFakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = [];

    public RoutedFakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_responder(request));
    }
}
