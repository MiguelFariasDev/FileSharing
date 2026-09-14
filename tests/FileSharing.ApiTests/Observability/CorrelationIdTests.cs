using FileSharing.Api.Middleware;

namespace FileSharing.ApiTests.Observability;

public class CorrelationIdTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CorrelationIdTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string?> GetHeaderValueAsync(HttpRequestMessage request)
    {
        var response = await _client.SendAsync(request);
        return response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values)
            ? values.SingleOrDefault()
            : null;
    }

    [Fact]
    public async Task Response_AlwaysCarriesACorrelationIdHeader_WhenNoneWasSent()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");

        var correlationId = await GetHeaderValueAsync(request);

        Assert.False(string.IsNullOrWhiteSpace(correlationId));
    }

    [Fact]
    public async Task Response_ReusesAValidClientSuppliedCorrelationId()
    {
        const string clientCorrelationId = "client-supplied-id-123";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, clientCorrelationId);

        var correlationId = await GetHeaderValueAsync(request);

        Assert.Equal(clientCorrelationId, correlationId);
    }

    [Fact]
    public async Task TwoRequestsWithNoHeader_GetDifferentCorrelationIds()
    {
        using var first = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        using var second = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");

        var firstId = await GetHeaderValueAsync(first);
        var secondId = await GetHeaderValueAsync(second);

        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public async Task OversizedClientCorrelationId_IsRejected_AndANewOneIsGeneratedInstead()
    {
        var tooLong = new string('a', 500);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, tooLong);

        var correlationId = await GetHeaderValueAsync(request);

        Assert.NotEqual(tooLong, correlationId);
        Assert.True(correlationId!.Length <= 128);
    }

    [Theory]
    [InlineData("has spaces")]
    [InlineData("has\ttab")]
    [InlineData("has;semicolon")]
    [InlineData("has\nnewline")]
    public async Task CorrelationIdWithDisallowedCharacters_IsRejected_AndANewOneIsGeneratedInstead(string invalidValue)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        // HttpRequestMessage rejects raw control characters outright at the client — TryAddWithoutValidation
        // is the only way to actually get a value like this onto the wire to exercise the server's own validation.
        request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, invalidValue);

        var correlationId = await GetHeaderValueAsync(request);

        Assert.NotEqual(invalidValue, correlationId);
    }

    [Fact]
    public async Task RepeatedCorrelationIdHeader_IsTreatedAsAmbiguous_AndANewOneIsGeneratedInstead()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "first-value");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "second-value");

        var response = await _client.SendAsync(request);
        var values = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).ToList();

        Assert.Single(values);
        Assert.DoesNotContain(values[0], new[] { "first-value", "second-value" });
    }
}
