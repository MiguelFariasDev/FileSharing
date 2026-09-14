namespace FileSharing.Mobile.Tests.Services;

/// <summary>Same pattern already used by FileSharing.Web.Tests — a routable fake
/// HttpMessageHandler so FileSharingApiClient/S3UploadHttpClient can be tested without a real
/// network call, and every request sent is captured for assertions (e.g. "the JWT was attached"
/// or, just as importantly, "the JWT was never attached to this specific request").</summary>
public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = [];

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_responder(request));
    }
}
