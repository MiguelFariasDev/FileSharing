using System.Net;

namespace FileSharing.Web.Tests.Services;

/// <summary>
/// Records the last request it saw and returns a fixed response — enough to test
/// FileSharingApiClient's own logic (headers attached, status-code mapping) without a real
/// Api/network call.
/// </summary>
public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly HttpContent? _content;

    public HttpRequestMessage? LastRequest { get; private set; }

    public FakeHttpMessageHandler(HttpStatusCode statusCode, HttpContent? content = null)
    {
        _statusCode = statusCode;
        _content = content;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(new HttpResponseMessage(_statusCode) { Content = _content });
    }
}
