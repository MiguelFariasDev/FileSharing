using System.Net.Http.Json;
using FileSharing.Application.DTOs.Auth;

namespace FileSharing.ApiTests.Auth;

/// <summary>
/// The mandatory anti-enumeration check for POST /api/auth/forgot-password: a registered email
/// and an unregistered one must produce an indistinguishable response — same status, same code
/// (there is none: this endpoint has no error path), same message, same shape. Timing is
/// intentionally not asserted here (any HTTP test's timing is far too noisy to assert on
/// reliably) — see docs/security.md for why that residual difference is accepted.
/// </summary>
public class PasswordResetEnumerationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public PasswordResetEnumerationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task ForgotPassword_ExistingAndNonexistentEmail_ProduceTheSameResponse()
    {
        var existingEmail = UniqueEmail();
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(existingEmail, "SenhaForte123"));

        var existingResponse = await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(existingEmail));
        var nonexistentResponse = await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(UniqueEmail()));

        Assert.Equal(existingResponse.StatusCode, nonexistentResponse.StatusCode);

        var existingBody = await existingResponse.Content.ReadAsStringAsync();
        var nonexistentBody = await nonexistentResponse.Content.ReadAsStringAsync();
        Assert.Equal(existingBody, nonexistentBody);
    }
}
