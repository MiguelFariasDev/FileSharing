using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FileSharing.Application.Abstractions.Notifications;
using FileSharing.Application.Abstractions.Storage;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Application.DTOs.Files;
using FileSharing.Application.DTOs.Notifications;
using FileSharing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FileSharing.ApiTests.Files;

public class PublicFileDownloadEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Password = "SenhaForte123";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PublicFileDownloadEndpointsTests(CustomWebApplicationFactory factory)
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

    private HttpRequestMessage AuthenticatedRequest(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private async Task<(Guid FileId, string AccessToken)> CreatePublicLinkAsync(string ownerAccessToken)
    {
        using var initiateRequest = AuthenticatedRequest(HttpMethod.Post, "/api/files/upload", ownerAccessToken);
        initiateRequest.Content = JsonContent.Create(new InitiateUploadRequest("document.pdf", "application/pdf", 1024, false));
        var initiateResponse = await _client.SendAsync(initiateRequest);
        Assert.Equal(HttpStatusCode.Created, initiateResponse.StatusCode);
        var initiated = (await initiateResponse.Content.ReadFromJsonAsync<InitiateUploadResponse>())!;

        _factory.FileStorageServiceMock
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageObjectMetadata(1024, "application/pdf"));

        using var completeRequest = AuthenticatedRequest(HttpMethod.Post, $"/api/files/{initiated.FileId}/complete", ownerAccessToken);
        var completeResponse = await _client.SendAsync(completeRequest);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        using var linkRequest = AuthenticatedRequest(HttpMethod.Post, $"/api/files/{initiated.FileId}/link", ownerAccessToken);
        var linkResponse = await _client.SendAsync(linkRequest);
        Assert.Equal(HttpStatusCode.OK, linkResponse.StatusCode);
        var link = await linkResponse.Content.ReadFromJsonAsync<JsonElement>();

        return (initiated.FileId, link.GetProperty("accessToken").GetString()!);
    }

    private async Task ExpireFileWithTokenAsync(string accessToken)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var accessTokenHash = FileSharing.Application.Common.AccessTokenHasher.Hash(accessToken);
        var file = await dbContext.Files.SingleAsync(f => f.AccessTokenHash == accessTokenHash);

        dbContext.Entry(file).Property("ExpiresAt").CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-1);
        await dbContext.SaveChangesAsync();
    }

    private void SetupObjectExists(bool exists = true) =>
        _factory.FileStorageServiceMock
            .Setup(s => s.ObjectExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exists);

    private void SetupPresignedDownloadUrl(string url = "https://mock-s3.test/download", DateTimeOffset? expiresAt = null) =>
        _factory.FileStorageServiceMock
            .Setup(s => s.CreatePresignedDownloadUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PresignedDownloadUrl(url, expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(5)));

    private async Task<List<FileSharing.Domain.Entities.Download>> GetDownloadsAsync(Guid fileId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.Downloads.Where(d => d.FileId == fileId).ToListAsync();
    }

    private async Task<Guid> GetOwnerUserIdAsync(Guid fileId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await dbContext.Files.SingleAsync(f => f.Id == fileId)).UserId;
    }

    [Fact]
    public async Task DownloadPublicFile_DoesNotRequireAuthentication()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var (_, accessToken) = await CreatePublicLinkAsync(ownerToken);
        SetupObjectExists();
        SetupPresignedDownloadUrl();

        // No Authorization header attached at all.
        var response = await _client.GetAsync($"/api/public/files/{accessToken}/download");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DownloadPublicFile_WithValidToken_ReturnsADownloadUrl()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var (_, accessToken) = await CreatePublicLinkAsync(ownerToken);
        SetupObjectExists();
        SetupPresignedDownloadUrl("https://mock-s3.test/download-here", DateTimeOffset.UtcNow.AddMinutes(5));

        var response = await _client.GetAsync($"/api/public/files/{accessToken}/download");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<DownloadUrlResponse>();
        Assert.NotNull(body);
        Assert.Equal("https://mock-s3.test/download-here", body!.DownloadUrl);
        Assert.True(body.ExpiresAt > DateTimeOffset.UtcNow);
        // Short-lived: nowhere near the file's own 24h ExpiresAt window.
        Assert.True(body.ExpiresAt < DateTimeOffset.UtcNow.AddHours(1));
    }

    [Fact]
    public async Task DownloadPublicFile_WithUnknownToken_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/public/files/this-token-was-never-issued-abc123/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadPublicFile_WithExpiredFilesToken_ReturnsNotFound()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var (_, accessToken) = await CreatePublicLinkAsync(ownerToken);
        await ExpireFileWithTokenAsync(accessToken);
        SetupObjectExists();

        var response = await _client.GetAsync($"/api/public/files/{accessToken}/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadPublicFile_WhenObjectIsMissingFromStorage_ReturnsNotFound()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var (_, accessToken) = await CreatePublicLinkAsync(ownerToken);
        SetupObjectExists(exists: false);

        var response = await _client.GetAsync($"/api/public/files/{accessToken}/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadPublicFile_WhenObjectIsMissingFromStorage_DoesNotRegisterADownload()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var (fileId, accessToken) = await CreatePublicLinkAsync(ownerToken);
        SetupObjectExists(exists: false);

        await _client.GetAsync($"/api/public/files/{accessToken}/download");

        Assert.Empty(await GetDownloadsAsync(fileId));
    }

    [Fact]
    public async Task DownloadPublicFile_UnknownAndExpiredTokens_ReturnTheSameGenericBody()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var (_, accessToken) = await CreatePublicLinkAsync(ownerToken);
        await ExpireFileWithTokenAsync(accessToken);
        SetupObjectExists();

        var expiredResponse = await _client.GetAsync($"/api/public/files/{accessToken}/download");
        var unknownResponse = await _client.GetAsync("/api/public/files/completely-made-up-token-xyz/download");

        Assert.Equal(expiredResponse.StatusCode, unknownResponse.StatusCode);

        var expiredBody = await expiredResponse.Content.ReadFromJsonAsync<JsonElement>();
        var unknownBody = await unknownResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expiredBody.GetProperty("status").GetInt32(), unknownBody.GetProperty("status").GetInt32());
        Assert.Equal(expiredBody.GetProperty("title").GetString(), unknownBody.GetProperty("title").GetString());
    }

    [Fact]
    public async Task DownloadPublicFile_ErrorResponse_DoesNotContainTheRequestedToken()
    {
        const string guessedToken = "super-secret-guessed-download-token";

        var response = await _client.GetAsync($"/api/public/files/{guessedToken}/download");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(guessedToken, body);
    }

    [Fact]
    public async Task DownloadPublicFile_SuccessResponse_DoesNotExposeTheAccessTokenHash()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var (_, accessToken) = await CreatePublicLinkAsync(ownerToken);
        SetupObjectExists();
        SetupPresignedDownloadUrl();

        var response = await _client.GetAsync($"/api/public/files/{accessToken}/download");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.TryGetProperty("accessTokenHash", out _));
        Assert.False(body.RootElement.TryGetProperty("accessToken", out _));
    }

    [Fact]
    public async Task DownloadPublicFile_RegistersADownload_WithTheFileIdAndAnIpAddress()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var (fileId, accessToken) = await CreatePublicLinkAsync(ownerToken);
        SetupObjectExists();
        SetupPresignedDownloadUrl();

        await _client.GetAsync($"/api/public/files/{accessToken}/download");

        var downloads = await GetDownloadsAsync(fileId);
        Assert.Single(downloads);
        Assert.Equal(fileId, downloads[0].FileId);
        Assert.False(string.IsNullOrWhiteSpace(downloads[0].IpAddress));
        Assert.Equal(TimeSpan.Zero, downloads[0].DownloadedAt.Offset);
    }

    [Fact]
    public async Task DownloadPublicFile_RegistersTheRequestsUserAgent()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var (fileId, accessToken) = await CreatePublicLinkAsync(ownerToken);
        SetupObjectExists();
        SetupPresignedDownloadUrl();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/public/files/{accessToken}/download");
        request.Headers.UserAgent.ParseAdd("IntegrationTestClient/1.0");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var downloads = await GetDownloadsAsync(fileId);
        Assert.Equal("IntegrationTestClient/1.0", downloads.Single().UserAgent);
    }

    [Fact]
    public async Task DownloadPublicFile_MissingUserAgent_DoesNotBreakTheDownload()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var (fileId, accessToken) = await CreatePublicLinkAsync(ownerToken);
        SetupObjectExists();
        SetupPresignedDownloadUrl();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/public/files/{accessToken}/download");
        // HttpClient/TestServer normally injects its own default User-Agent unless explicitly
        // suppressed — clear it to genuinely exercise the "header absent" path.
        request.Headers.UserAgent.Clear();

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var downloads = await GetDownloadsAsync(fileId);
        Assert.False(string.IsNullOrWhiteSpace(downloads.Single().UserAgent));
    }

    [Fact]
    public async Task DownloadPublicFile_CalledMultipleTimes_RegistersASeparateDownloadEachTime()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var (fileId, accessToken) = await CreatePublicLinkAsync(ownerToken);
        SetupObjectExists();
        SetupPresignedDownloadUrl();

        await _client.GetAsync($"/api/public/files/{accessToken}/download");
        await _client.GetAsync($"/api/public/files/{accessToken}/download");

        var downloads = await GetDownloadsAsync(fileId);
        Assert.Equal(2, downloads.Count);
        Assert.Equal(2, downloads.Select(d => d.Id).Distinct().Count());
    }

    [Fact]
    public async Task DownloadPublicFile_ThereIsNoRouteToDownloadByFileIdAlone()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var (fileId, _) = await CreatePublicLinkAsync(ownerToken);

        // Guards against ever reintroducing a FileId-based download route: the token is the
        // only valid authorization mechanism for the public download flow.
        using var request = AuthenticatedRequest(HttpMethod.Get, $"/api/files/{fileId}/download", ownerToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Fase 7: notificação de download via IFileDownloadNotifier ---

    [Fact]
    public async Task DownloadPublicFile_ValidDownload_NotifiesTheOwner()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var (fileId, accessToken) = await CreatePublicLinkAsync(ownerToken);
        var ownerUserId = await GetOwnerUserIdAsync(fileId);
        SetupObjectExists();
        SetupPresignedDownloadUrl();

        var response = await _client.GetAsync($"/api/public/files/{accessToken}/download");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        _factory.FileDownloadNotifierMock.Verify(
            n => n.NotifyDownloadAsync(ownerUserId, It.Is<FileDownloadedNotification>(p => p.FileId == fileId), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DownloadPublicFile_WithExpiredFilesToken_NeverNotifiesTheOwner()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var (fileId, accessToken) = await CreatePublicLinkAsync(ownerToken);
        var ownerUserId = await GetOwnerUserIdAsync(fileId);
        await ExpireFileWithTokenAsync(accessToken);
        SetupObjectExists();

        var response = await _client.GetAsync($"/api/public/files/{accessToken}/download");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        _factory.FileDownloadNotifierMock.Verify(
            n => n.NotifyDownloadAsync(ownerUserId, It.Is<FileDownloadedNotification>(p => p.FileId == fileId), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DownloadPublicFile_WithUnknownToken_NeverCallsTheNotifier()
    {
        // FileDownloadNotifierMock is shared (IClassFixture) across every test in this class,
        // so a broad It.IsAny<Guid>() "never called" assertion must start from a clean slate —
        // otherwise it would fail on invocations legitimately made by earlier, unrelated tests.
        _factory.FileDownloadNotifierMock.Invocations.Clear();

        var response = await _client.GetAsync("/api/public/files/completely-made-up-token-for-notifier-test/download");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        _factory.FileDownloadNotifierMock.Verify(
            n => n.NotifyDownloadAsync(It.IsAny<Guid>(), It.IsAny<FileDownloadedNotification>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DownloadPublicFile_StillSucceeds_WhenTheNotifierIsUnavailable()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var (_, accessToken) = await CreatePublicLinkAsync(ownerToken);
        SetupObjectExists();
        SetupPresignedDownloadUrl();
        _factory.FileDownloadNotifierMock
            .Setup(n => n.NotifyDownloadAsync(It.IsAny<Guid>(), It.IsAny<FileDownloadedNotification>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated SignalR outage"));

        var response = await _client.GetAsync($"/api/public/files/{accessToken}/download");

        // The whole point of Phase 7's critical requirement: a notifier failure must never
        // turn an already-authorized download into an error response.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DownloadUrlResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.DownloadUrl));
    }
}
