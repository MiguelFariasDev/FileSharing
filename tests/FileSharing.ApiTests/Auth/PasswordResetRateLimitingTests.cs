using System.Net;
using System.Net.Http.Json;
using FileSharing.Application.DTOs.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace FileSharing.ApiTests.Auth;

/// <summary>
/// Same isolated-host pattern as AuthRateLimitingTests, for forgot-password's own, tighter
/// policy (RateLimiterPolicyNames.PasswordReset) — see that class's own remarks.
/// </summary>
public class PasswordResetRateLimitingTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PasswordResetRateLimitingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWithLowLimit(int permitLimit) =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configBuilder) =>
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:PasswordReset:PermitLimit"] = permitLimit.ToString()
                })))
            .CreateClient();

    [Fact]
    public async Task ForgotPassword_IsSubjectToItsOwnTighterPerIpLimit()
    {
        using var client = CreateClientWithLowLimit(permitLimit: 3);

        HttpResponseMessage? last = null;
        for (var i = 0; i < 4; i++)
        {
            last = await client.PostAsJsonAsync(
                "/api/auth/forgot-password",
                new ForgotPasswordRequest($"rate-limit-probe-{Guid.NewGuid():N}@example.com"));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
    }
}
