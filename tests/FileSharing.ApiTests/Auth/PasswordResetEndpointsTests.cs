using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FileSharing.Application.Common;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Domain.Entities;
using FileSharing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FileSharing.ApiTests.Auth;

public class PasswordResetEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Password = "SenhaForte123";
    private const string NewPassword = "NovaSenhaForte456";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PasswordResetEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private async Task<string> RegisterAsync(string email) =>
        (await (await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, Password)))
            .Content.ReadFromJsonAsync<UserResponse>())!.Id.ToString();

    /// <summary>Inserts a token directly (bypassing the API) so its expiry/used state can be controlled precisely.</summary>
    private async Task<string> SeedTokenAsync(Guid userId, DateTimeOffset createdAtUtc, TimeSpan validFor, bool used = false)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var plaintextToken = $"test-token-{Guid.NewGuid():N}";
        var tokenHash = AccessTokenHasher.Hash(plaintextToken);
        var resetToken = new PasswordResetToken(userId, tokenHash, createdAtUtc, validFor);

        if (used)
            resetToken.MarkAsUsed(createdAtUtc.Add(validFor / 2));

        dbContext.PasswordResetTokens.Add(resetToken);
        await dbContext.SaveChangesAsync();

        return plaintextToken;
    }

    private async Task<Guid> GetUserIdAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await dbContext.Users.SingleAsync(u => u.Email == email)).Id;
    }

    [Fact]
    public async Task ForgotPassword_WithExistingEmail_Returns202AndSendsAnEmail()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        _factory.EmailServiceMock.Invocations.Clear();

        var response = await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        _factory.EmailServiceMock.Verify(
            s => s.SendPasswordResetEmailAsync(email, It.Is<string>(link => link.Contains("token=")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ForgotPassword_WithUnknownEmail_Returns202AndSendsNoEmail()
    {
        _factory.EmailServiceMock.Invocations.Clear();

        var response = await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(UniqueEmail()));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        _factory.EmailServiceMock.Verify(
            s => s.SendPasswordResetEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ForgotPassword_IssuingASecondToken_InvalidatesTheFirst()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);

        await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));
        var firstLink = GetCapturedLink();

        await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));

        var firstToken = new Uri(firstLink).Query.Split("token=")[1];
        var response = await _client.GetAsync($"/api/auth/reset-password/{firstToken}");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        await AssertCodeAsync(response, "AUTH_PASSWORD_RESET_USED");

        string GetCapturedLink()
        {
            var invocation = _factory.EmailServiceMock.Invocations.Last();
            return (string)invocation.Arguments[1];
        }
    }

    [Fact]
    public async Task ValidateResetToken_WithValidToken_ReturnsOk()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        var userId = await GetUserIdAsync(email);
        var token = await SeedTokenAsync(userId, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));

        var response = await _client.GetAsync($"/api/auth/reset-password/{token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ValidateResetToken_WithUnknownToken_ReturnsNotFoundWithInvalidCode()
    {
        var response = await _client.GetAsync("/api/auth/reset-password/this-token-does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertCodeAsync(response, "AUTH_PASSWORD_RESET_INVALID");
    }

    [Fact]
    public async Task ValidateResetToken_WithExpiredToken_ReturnsGoneWithExpiredCode()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        var userId = await GetUserIdAsync(email);
        var token = await SeedTokenAsync(userId, DateTimeOffset.UtcNow.AddHours(-2), TimeSpan.FromMinutes(30));

        var response = await _client.GetAsync($"/api/auth/reset-password/{token}");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        await AssertCodeAsync(response, "AUTH_PASSWORD_RESET_EXPIRED");
    }

    [Fact]
    public async Task ValidateResetToken_WithUsedToken_ReturnsGoneWithUsedCode()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        var userId = await GetUserIdAsync(email);
        var token = await SeedTokenAsync(userId, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30), used: true);

        var response = await _client.GetAsync($"/api/auth/reset-password/{token}");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        await AssertCodeAsync(response, "AUTH_PASSWORD_RESET_USED");
    }

    [Fact]
    public async Task ResetPassword_WithValidToken_ChangesPasswordAndOldPasswordStopsWorking()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        var userId = await GetUserIdAsync(email);
        var token = await SeedTokenAsync(userId, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));

        var resetResponse = await _client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(token, NewPassword));
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);

        var oldLoginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        Assert.Equal(HttpStatusCode.Unauthorized, oldLoginResponse.StatusCode);

        var newLoginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, NewPassword));
        Assert.Equal(HttpStatusCode.OK, newLoginResponse.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_TokenCannotBeReused()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        var userId = await GetUserIdAsync(email);
        var token = await SeedTokenAsync(userId, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));

        var first = await _client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(token, NewPassword));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await _client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(token, "OutraSenhaForte789"));

        Assert.Equal(HttpStatusCode.Gone, second.StatusCode);
        await AssertCodeAsync(second, "AUTH_PASSWORD_RESET_USED");
    }

    [Fact]
    public async Task ResetPassword_WithWeakPassword_ReturnsValidationError()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        var userId = await GetUserIdAsync(email);
        var token = await SeedTokenAsync(userId, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));

        var response = await _client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(token, "short"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertCodeAsync(response, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task ResetPassword_WithExpiredToken_DoesNotChangeThePassword()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        var userId = await GetUserIdAsync(email);
        var token = await SeedTokenAsync(userId, DateTimeOffset.UtcNow.AddHours(-2), TimeSpan.FromMinutes(30));

        var response = await _client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(token, NewPassword));
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);

        var loginWithOldPassword = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        Assert.Equal(HttpStatusCode.OK, loginWithOldPassword.StatusCode);
    }

    private static async Task AssertCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
    }
}
