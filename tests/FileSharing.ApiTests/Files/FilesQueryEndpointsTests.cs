using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FileSharing.Application.Abstractions.Storage;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Application.DTOs.Files;
using Moq;

namespace FileSharing.ApiTests.Files;

/// <summary>
/// GET /api/files/mine and GET /api/files/{id}/downloads — added in Etapa 8 to back the
/// dashboard. Both are owner-scoped exactly like the existing complete/link endpoints.
/// </summary>
public class FilesQueryEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Password = "SenhaForte123";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public FilesQueryEndpointsTests(CustomWebApplicationFactory factory)
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

    private async Task<Guid> InitiateUploadAsync(string accessToken)
    {
        using var request = AuthenticatedRequest(HttpMethod.Post, "/api/files/upload", accessToken);
        request.Content = JsonContent.Create(new InitiateUploadRequest("document.pdf", "application/pdf", 1024, false));
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InitiateUploadResponse>())!.FileId;
    }

    private async Task<Guid> CreateActiveFileAsync(string accessToken)
    {
        var fileId = await InitiateUploadAsync(accessToken);

        _factory.FileStorageServiceMock
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageObjectMetadata(1024, "application/pdf"));

        using var completeRequest = AuthenticatedRequest(HttpMethod.Post, $"/api/files/{fileId}/complete", accessToken);
        var completeResponse = await _client.SendAsync(completeRequest);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        return fileId;
    }

    private async Task<string> GenerateLinkAsync(Guid fileId, string accessToken)
    {
        using var request = AuthenticatedRequest(HttpMethod.Post, $"/api/files/{fileId}/link", accessToken);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    // --- GET /api/files/mine ---

    [Fact]
    public async Task GetMyFiles_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/files/mine");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMyFiles_NoFiles_ReturnsEmptyList()
    {
        var token = await RegisterAndLoginAsync();

        using var request = AuthenticatedRequest(HttpMethod.Get, "/api/files/mine", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var files = await response.Content.ReadFromJsonAsync<List<FileSummaryResponse>>();
        Assert.Empty(files!);
    }

    [Fact]
    public async Task GetMyFiles_NeverReturnsFilesBelongingToAnotherUser()
    {
        var userAToken = await RegisterAndLoginAsync();
        await CreateActiveFileAsync(userAToken);

        var userBToken = await RegisterAndLoginAsync();
        await CreateActiveFileAsync(userBToken);

        using var request = AuthenticatedRequest(HttpMethod.Get, "/api/files/mine", userBToken);
        var response = await _client.SendAsync(request);

        var files = await response.Content.ReadFromJsonAsync<List<FileSummaryResponse>>();
        Assert.Single(files!);
    }

    [Fact]
    public async Task GetMyFiles_ReportsActiveStatus_AndPublicLinkPresence()
    {
        var token = await RegisterAndLoginAsync();
        var fileId = await CreateActiveFileAsync(token);
        await GenerateLinkAsync(fileId, token);

        using var request = AuthenticatedRequest(HttpMethod.Get, "/api/files/mine", token);
        var response = await _client.SendAsync(request);

        var file = (await response.Content.ReadFromJsonAsync<List<FileSummaryResponse>>())!.Single();
        Assert.Equal(fileId, file.FileId);
        Assert.Equal("Active", file.Status);
        Assert.True(file.HasPublicLink);
        Assert.NotNull(file.ExpiresAt);
    }

    [Fact]
    public async Task GetMyFiles_ReportsPendingUploadStatus_WithoutPublicLink()
    {
        var token = await RegisterAndLoginAsync();
        await InitiateUploadAsync(token);

        using var request = AuthenticatedRequest(HttpMethod.Get, "/api/files/mine", token);
        var response = await _client.SendAsync(request);

        var file = (await response.Content.ReadFromJsonAsync<List<FileSummaryResponse>>())!.Single();
        Assert.Equal("PendingUpload", file.Status);
        Assert.False(file.HasPublicLink);
        Assert.Null(file.ExpiresAt);
    }

    [Fact]
    public async Task GetMyFiles_NeverExposesAccessTokenOrHash()
    {
        var token = await RegisterAndLoginAsync();
        var fileId = await CreateActiveFileAsync(token);
        await GenerateLinkAsync(fileId, token);

        using var request = AuthenticatedRequest(HttpMethod.Get, "/api/files/mine", token);
        var response = await _client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("accessToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", body, StringComparison.OrdinalIgnoreCase);
    }

    // --- GET /api/files/{id}/downloads ---

    [Fact]
    public async Task GetDownloadHistory_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync($"/api/files/{Guid.NewGuid()}/downloads");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDownloadHistory_NonexistentFile_ReturnsNotFound()
    {
        var token = await RegisterAndLoginAsync();

        using var request = AuthenticatedRequest(HttpMethod.Get, $"/api/files/{Guid.NewGuid()}/downloads", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDownloadHistory_FileOfAnotherUser_ReturnsNotFound()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var fileId = await CreateActiveFileAsync(ownerToken);

        var otherUserToken = await RegisterAndLoginAsync();

        using var request = AuthenticatedRequest(HttpMethod.Get, $"/api/files/{fileId}/downloads", otherUserToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDownloadHistory_OwnFile_ReturnsHistory()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var fileId = await CreateActiveFileAsync(ownerToken);
        var publicToken = await GenerateLinkAsync(fileId, ownerToken);

        _factory.FileStorageServiceMock
            .Setup(s => s.ObjectExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _factory.FileStorageServiceMock
            .Setup(s => s.CreatePresignedDownloadUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PresignedDownloadUrl("https://mock-s3.test/download", DateTimeOffset.UtcNow.AddMinutes(5)));

        var downloadResponse = await _client.GetAsync($"/api/public/files/{publicToken}/download");
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);

        using var historyRequest = AuthenticatedRequest(HttpMethod.Get, $"/api/files/{fileId}/downloads", ownerToken);
        var historyResponse = await _client.SendAsync(historyRequest);

        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = await historyResponse.Content.ReadFromJsonAsync<List<DownloadHistoryEntryResponse>>();
        Assert.Single(history!);
    }

    [Fact]
    public async Task GetDownloadHistory_NeverExposesIpOrUserAgent()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var fileId = await CreateActiveFileAsync(ownerToken);
        var publicToken = await GenerateLinkAsync(fileId, ownerToken);

        _factory.FileStorageServiceMock
            .Setup(s => s.ObjectExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _factory.FileStorageServiceMock
            .Setup(s => s.CreatePresignedDownloadUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PresignedDownloadUrl("https://mock-s3.test/download", DateTimeOffset.UtcNow.AddMinutes(5)));

        await _client.GetAsync($"/api/public/files/{publicToken}/download");

        using var historyRequest = AuthenticatedRequest(HttpMethod.Get, $"/api/files/{fileId}/downloads", ownerToken);
        var historyResponse = await _client.SendAsync(historyRequest);

        var body = await historyResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ipAddress", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userAgent", body, StringComparison.OrdinalIgnoreCase);
    }
}
