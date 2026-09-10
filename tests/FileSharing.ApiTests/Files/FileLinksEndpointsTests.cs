using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FileSharing.Application.Abstractions.Storage;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Application.DTOs.Files;
using Moq;

namespace FileSharing.ApiTests.Files;

public class FileLinksEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Password = "SenhaForte123";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public FileLinksEndpointsTests(CustomWebApplicationFactory factory)
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

    private async Task<InitiateUploadResponse> InitiateUploadAsync(string accessToken)
    {
        using var request = AuthenticatedRequest(HttpMethod.Post, "/api/files/upload", accessToken);
        request.Content = JsonContent.Create(new InitiateUploadRequest("document.pdf", "application/pdf", 1024, false));

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<InitiateUploadResponse>())!;
    }

    private async Task<Guid> CreateActiveFileAsync(string accessToken)
    {
        var initiated = await InitiateUploadAsync(accessToken);

        _factory.FileStorageServiceMock
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageObjectMetadata(1024, "application/pdf"));

        using var completeRequest = AuthenticatedRequest(HttpMethod.Post, $"/api/files/{initiated.FileId}/complete", accessToken);
        var completeResponse = await _client.SendAsync(completeRequest);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        return initiated.FileId;
    }

    [Fact]
    public async Task GenerateLink_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.PostAsync($"/api/files/{Guid.NewGuid()}/link", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GenerateLink_NonexistentFile_ReturnsNotFound()
    {
        var token = await RegisterAndLoginAsync();

        using var request = AuthenticatedRequest(HttpMethod.Post, $"/api/files/{Guid.NewGuid()}/link", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GenerateLink_FileOfAnotherUser_ReturnsNotFound()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var fileId = await CreateActiveFileAsync(ownerToken);

        var otherUserToken = await RegisterAndLoginAsync();

        using var request = AuthenticatedRequest(HttpMethod.Post, $"/api/files/{fileId}/link", otherUserToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GenerateLink_ForPendingUpload_ReturnsConflict()
    {
        var token = await RegisterAndLoginAsync();
        var initiated = await InitiateUploadAsync(token);

        using var request = AuthenticatedRequest(HttpMethod.Post, $"/api/files/{initiated.FileId}/link", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GenerateLink_ForOwnActiveFile_ReturnsAccessTokenAndPublicUrl()
    {
        var token = await RegisterAndLoginAsync();
        var fileId = await CreateActiveFileAsync(token);

        using var request = AuthenticatedRequest(HttpMethod.Post, $"/api/files/{fileId}/link", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        Assert.Equal(fileId, root.GetProperty("fileId").GetGuid());

        var accessToken = root.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        Assert.True(accessToken!.Length >= 22);

        var publicUrl = root.GetProperty("publicUrl").GetString();
        Assert.False(string.IsNullOrWhiteSpace(publicUrl));
        Assert.Contains($"/api/public/files/{accessToken}", publicUrl);

        // The response must never leak the persisted hash — only the plaintext token.
        Assert.False(root.TryGetProperty("accessTokenHash", out _));
    }

    [Fact]
    public async Task GenerateLink_CalledTwice_ReturnsADifferentTokenEachTime()
    {
        var token = await RegisterAndLoginAsync();
        var fileId = await CreateActiveFileAsync(token);

        using var first = AuthenticatedRequest(HttpMethod.Post, $"/api/files/{fileId}/link", token);
        var firstResponse = await _client.SendAsync(first);
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();

        using var second = AuthenticatedRequest(HttpMethod.Post, $"/api/files/{fileId}/link", token);
        var secondResponse = await _client.SendAsync(second);
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.NotEqual(firstBody.GetProperty("accessToken").GetString(), secondBody.GetProperty("accessToken").GetString());
    }
}
