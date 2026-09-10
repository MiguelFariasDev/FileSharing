using System.Net;

namespace FileSharing.ApiTests.Files;

/// <summary>
/// Isolated in its own test class (and therefore its own CustomWebApplicationFactory/host
/// instance) on purpose: the "public-files" rate limiter (Program.cs) is a single, global,
/// unpartitioned fixed-window counter shared by every request that hits an endpoint carrying
/// the policy — including both GET /api/public/files/{token} and .../download, since they're
/// both declared on PublicFilesController with [EnableRateLimiting] at the class level. Running
/// this alongside the functional tests in PublicFilesEndpointsTests/PublicFileDownloadEndpointsTests
/// would burn through the same budget those tests rely on staying under, making them flaky.
/// </summary>
public class PublicFilesRateLimitingTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public PublicFilesRateLimitingTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task DownloadEndpoint_IsSubjectToThePublicFilesRateLimitPolicy()
    {
        // Program.cs: AddFixedWindowLimiter(RateLimiterPolicyNames.PublicFiles, PermitLimit: 30).
        // Any token works here — the limiter runs before the request reaches the action, so it
        // counts regardless of whether the token would have resolved to anything.
        HttpResponseMessage? last = null;
        for (var i = 0; i < 31; i++)
            last = await _client.GetAsync($"/api/public/files/rate-limit-probe-{i}/download");

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
    }
}
