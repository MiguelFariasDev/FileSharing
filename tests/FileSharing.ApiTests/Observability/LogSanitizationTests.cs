using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FileSharing.Application.Abstractions.Storage;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Application.DTOs.Files;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace FileSharing.ApiTests.Observability;

/// <summary>
/// Drives a realistic flow (register -> login -> upload -> complete -> generate link ->
/// public access -> public download) against an isolated host with its own CapturingLoggerProvider,
/// then asserts on the actual rendered log output — not on reading the source code — that none of
/// it ever contains the JWT, the Authorization header, the password, the plaintext access token,
/// or the presigned URL/StorageKey. Isolated via WithWebHostBuilder (its own host, own captured
/// logs) rather than the shared CustomWebApplicationFactory, so this never captures noise from
/// other tests running concurrently against the same factory.
/// </summary>
public class LogSanitizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Password = "SenhaForte123";

    private readonly CustomWebApplicationFactory _baseFactory;

    public LogSanitizationTests(CustomWebApplicationFactory factory)
    {
        _baseFactory = factory;
    }

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task FullFlow_NeverLogsTheJwtPasswordAccessTokenOrPresignedUrl()
    {
        var loggerProvider = new CapturingLoggerProvider();

        using var factory = _baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.AddProvider(loggerProvider)));
        using var client = factory.CreateClient();

        var email = UniqueEmail();

        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, Password));
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        var login = (await loginResponse.Content.ReadFromJsonAsync<AuthResponse>())!;
        var jwt = login.AccessToken;

        HttpRequestMessage Authenticated(HttpMethod method, string url)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            return request;
        }

        using var initiateRequest = Authenticated(HttpMethod.Post, "/api/files/upload");
        initiateRequest.Content = JsonContent.Create(new InitiateUploadRequest("document.pdf", "application/pdf", 1024, false));
        var initiated = (await (await client.SendAsync(initiateRequest)).Content.ReadFromJsonAsync<InitiateUploadResponse>())!;
        var presignedUploadUrl = initiated.UploadUrl;

        _baseFactory.FileStorageServiceMock
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageObjectMetadata(1024, "application/pdf"));
        _baseFactory.FileStorageServiceMock
            .Setup(s => s.ObjectExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _baseFactory.FileStorageServiceMock
            .Setup(s => s.CreatePresignedDownloadUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PresignedDownloadUrl("https://mock-s3.test/download?X-Amz-Signature=abc123signature", DateTimeOffset.UtcNow.AddMinutes(5)));

        using var completeRequest = Authenticated(HttpMethod.Post, $"/api/files/{initiated.FileId}/complete");
        await client.SendAsync(completeRequest);

        using var linkRequest = Authenticated(HttpMethod.Post, $"/api/files/{initiated.FileId}/link");
        var linkResponse = await client.SendAsync(linkRequest);
        var linkBody = await linkResponse.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = linkBody.GetProperty("accessToken").GetString()!;

        await client.GetAsync($"/api/public/files/{accessToken}");
        var downloadResponse = await client.GetAsync($"/api/public/files/{accessToken}/download");
        var downloadBody = await downloadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var presignedDownloadUrl = downloadBody.GetProperty("downloadUrl").GetString()!;

        // GetDownloadHistory as the owner too — exercises one more authenticated, JWT-bearing
        // call before inspecting everything that got logged along the way.
        using var historyRequest = Authenticated(HttpMethod.Get, $"/api/files/{initiated.FileId}/downloads");
        await client.SendAsync(historyRequest);

        var allLogs = string.Join('\n', loggerProvider.Messages);

        Assert.DoesNotContain(jwt, allLogs);
        Assert.DoesNotContain(Password, allLogs);
        Assert.DoesNotContain("Bearer ", allLogs);
        Assert.DoesNotContain(accessToken, allLogs);
        Assert.DoesNotContain(presignedUploadUrl, allLogs);
        Assert.DoesNotContain(presignedDownloadUrl, allLogs);
        Assert.DoesNotContain("X-Amz-Signature", allLogs);

        // Sanity check that this test actually captured something meaningful, rather than the
        // provider silently capturing nothing and every assertion above passing vacuously —
        // FileId is explicitly the identifier every log line in this flow is expected to use.
        Assert.Contains(initiated.FileId.ToString(), allLogs);
    }
}
