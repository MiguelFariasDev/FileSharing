using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FileSharing.Api.Middleware;
using FileSharing.Application.Abstractions.Storage;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Application.DTOs.Files;
using Moq;

namespace FileSharing.ApiTests;

/// <summary>
/// Forces a genuinely unhandled exception (as opposed to every other test in this project, which
/// exercises expected failures already mapped to their own status code by the controllers) to
/// prove GlobalExceptionHandler — not the framework's own bare-500-with-empty-body default — is
/// what actually answers, and that it never leaks the real exception back to the client.
/// </summary>
public class ExceptionHandlingTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Password = "SenhaForte123";
    private const string SensitiveDetail = "Host=prod-db.internal;Password=hunter2";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ExceptionHandlingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = UniqueEmail();
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, Password));
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        var login = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        return login!.AccessToken;
    }

    [Fact]
    public async Task UnhandledException_ReturnsAGenericProblemDetails_WithoutLeakingTheRealException()
    {
        var token = await RegisterAndLoginAsync();

        using var initiateRequest = new HttpRequestMessage(HttpMethod.Post, "/api/files/upload");
        initiateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        initiateRequest.Content = JsonContent.Create(new InitiateUploadRequest("document.pdf", "application/pdf", 1024, false));
        var initiated = (await (await _client.SendAsync(initiateRequest)).Content.ReadFromJsonAsync<InitiateUploadResponse>())!;

        // Simulates a genuine infrastructure failure (e.g. S3 throwing something other than the
        // "not found" it normally handles) rather than a "not found"/mismatch outcome the
        // service already turns into a 404/409 on purpose.
        _factory.FileStorageServiceMock
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException($"simulated storage outage: {SensitiveDetail}"));

        using var completeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/files/{initiated.FileId}/complete");
        completeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        completeRequest.Headers.Add(CorrelationIdMiddleware.HeaderName, "exception-flow-correlation-id");
        var response = await _client.SendAsync(completeRequest);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(SensitiveDetail, body);
        Assert.DoesNotContain("hunter2", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain("at FileSharing.", body); // a .NET stack trace frame
        Assert.DoesNotContain("simulated storage outage", body);

        // The correlation id must survive an unhandled exception both as a response header
        // (CorrelationIdMiddleware, set before the exception ever occurs) and inside the
        // ProblemDetails body itself (the CustomizeProblemDetails callback, Program.cs) — so a
        // caller reporting "I got a 500" always has something to hand back for a log lookup.
        Assert.Equal("exception-flow-correlation-id", response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());

        using var problemDetails = JsonDocument.Parse(body);
        Assert.Equal("exception-flow-correlation-id", problemDetails.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Responses_CarryTheExpectedSecurityHeaders()
    {
        var response = await _client.GetAsync("/api/auth/me");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.True(response.Headers.Contains("Permissions-Policy"));
    }
}
