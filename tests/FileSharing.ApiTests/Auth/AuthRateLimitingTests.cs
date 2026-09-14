using System.Net;
using System.Net.Http.Json;
using FileSharing.Application.DTOs.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace FileSharing.ApiTests.Auth;

/// <summary>
/// CustomWebApplicationFactory raises RateLimiting:Auth:PermitLimit to an effectively-unlimited
/// value (see its own comment) so the rest of this project — most of which logs in at least
/// once per test — isn't flaky against the real per-IP limit. This class uses
/// WithWebHostBuilder to spin up its own isolated host (own rate limiter state, own in-memory
/// database) with that value overridden back down, so it can actually prove
/// RateLimiterPolicyNames.Auth (Program.cs) rejects excess requests instead of just asserting
/// against a mocked-away policy.
/// </summary>
public class AuthRateLimitingTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthRateLimitingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWithLowAuthLimit(int permitLimit) =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configBuilder) =>
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:Auth:PermitLimit"] = permitLimit.ToString()
                })))
            .CreateClient();

    [Fact]
    public async Task Register_IsSubjectToThePerIpAuthRateLimit()
    {
        using var client = CreateClientWithLowAuthLimit(permitLimit: 3);

        HttpResponseMessage? last = null;
        for (var i = 0; i < 4; i++)
        {
            last = await client.PostAsJsonAsync(
                "/api/auth/register",
                new RegisterRequest($"rate-limit-probe-{Guid.NewGuid():N}@example.com", "SenhaForte123"));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
    }

    [Fact]
    public async Task Login_IsSubjectToThePerIpAuthRateLimit()
    {
        // Deliberately all against the *same* nonexistent account — the limiter must trip
        // regardless of whether the credentials are ever valid, since the whole point is
        // throttling guesses before they can be checked against real accounts.
        using var client = CreateClientWithLowAuthLimit(permitLimit: 3);

        HttpResponseMessage? last = null;
        for (var i = 0; i < 4; i++)
        {
            last = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest("nobody@example.com", "wrong-password"));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
    }
}
