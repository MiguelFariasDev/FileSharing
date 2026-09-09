using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FileSharing.Application.Abstractions.Storage;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Application.DTOs.Files;
using Moq;

namespace FileSharing.ApiTests.Files;

public class FilesEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Password = "SenhaForte123";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public FilesEndpointsTests(CustomWebApplicationFactory factory)
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

    private async Task<InitiateUploadResponse> InitiateUploadAsync(string accessToken, InitiateUploadRequest body)
    {
        using var request = AuthenticatedRequest(HttpMethod.Post, "/api/files/upload", accessToken);
        request.Content = JsonContent.Create(body);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<InitiateUploadResponse>())!;
    }

    [Fact]
    public async Task InitiateUpload_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/files/upload",
            new InitiateUploadRequest("document.pdf", "application/pdf", 1024, false));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InitiateUpload_WithAllowedType_ReturnsCreatedWithUploadUrl()
    {
        var token = await RegisterAndLoginAsync();

        using var request = AuthenticatedRequest(HttpMethod.Post, "/api/files/upload", token);
        request.Content = JsonContent.Create(new InitiateUploadRequest("document.pdf", "application/pdf", 1024, false));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<InitiateUploadResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body!.FileId);
        Assert.False(string.IsNullOrWhiteSpace(body.UploadUrl));
    }

    [Fact]
    public async Task InitiateUpload_WithDisallowedContentType_ReturnsBadRequest()
    {
        var token = await RegisterAndLoginAsync();

        using var request = AuthenticatedRequest(HttpMethod.Post, "/api/files/upload", token);
        request.Content = JsonContent.Create(new InitiateUploadRequest("malware.exe", "application/x-msdownload", 1024, false));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InitiateUpload_WithExtensionNotMatchingContentType_ReturnsBadRequest()
    {
        var token = await RegisterAndLoginAsync();

        // Content-Type é de PDF, mas o nome do arquivo declara um executável.
        using var request = AuthenticatedRequest(HttpMethod.Post, "/api/files/upload", token);
        request.Content = JsonContent.Create(new InitiateUploadRequest("document.exe", "application/pdf", 1024, false));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InitiateUpload_WithSizeAboveLimit_ReturnsBadRequest()
    {
        var token = await RegisterAndLoginAsync();

        using var request = AuthenticatedRequest(HttpMethod.Post, "/api/files/upload", token);
        request.Content = JsonContent.Create(new InitiateUploadRequest("movie.mp4", "video/mp4", 6_000_000_000L, false));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CompleteUpload_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.PostAsync($"/api/files/{Guid.NewGuid()}/complete", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CompleteUpload_NonexistentFile_ReturnsNotFound()
    {
        var token = await RegisterAndLoginAsync();

        using var request = AuthenticatedRequest(HttpMethod.Post, $"/api/files/{Guid.NewGuid()}/complete", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CompleteUpload_FileOfAnotherUser_ReturnsNotFound()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var initiated = await InitiateUploadAsync(ownerToken, new InitiateUploadRequest("document.pdf", "application/pdf", 1024, false));

        var otherUserToken = await RegisterAndLoginAsync();

        using var request = AuthenticatedRequest(HttpMethod.Post, $"/api/files/{initiated.FileId}/complete", otherUserToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CompleteUpload_WhenObjectMissingInStorage_ReturnsConflict()
    {
        var token = await RegisterAndLoginAsync();
        var initiated = await InitiateUploadAsync(token, new InitiateUploadRequest("document.pdf", "application/pdf", 1024, false));

        // O mock padrão da factory já retorna null para GetObjectMetadataAsync (objeto ausente).
        using var request = AuthenticatedRequest(HttpMethod.Post, $"/api/files/{initiated.FileId}/complete", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CompleteUpload_WhenSizeMismatches_ReturnsConflict()
    {
        var token = await RegisterAndLoginAsync();
        var initiated = await InitiateUploadAsync(token, new InitiateUploadRequest("document.pdf", "application/pdf", 1024, false));

        _factory.FileStorageServiceMock
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageObjectMetadata(999, "application/pdf"));

        using var request = AuthenticatedRequest(HttpMethod.Post, $"/api/files/{initiated.FileId}/complete", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CompleteUpload_WithValidObject_ActivatesFile()
    {
        var token = await RegisterAndLoginAsync();
        var initiated = await InitiateUploadAsync(token, new InitiateUploadRequest("document.pdf", "application/pdf", 1024, false));

        _factory.FileStorageServiceMock
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageObjectMetadata(1024, "application/pdf"));

        using var request = AuthenticatedRequest(HttpMethod.Post, $"/api/files/{initiated.FileId}/complete", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CompleteUploadResponse>();
        Assert.NotNull(body);
        Assert.Equal(initiated.FileId, body!.FileId);
        Assert.Equal(body.CreatedAt.AddHours(24), body.ExpiresAt);
    }

    [Fact]
    public async Task CompleteUpload_CalledTwice_SecondCallReturnsConflict()
    {
        var token = await RegisterAndLoginAsync();
        var initiated = await InitiateUploadAsync(token, new InitiateUploadRequest("document.pdf", "application/pdf", 1024, false));

        _factory.FileStorageServiceMock
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageObjectMetadata(1024, "application/pdf"));

        using var first = AuthenticatedRequest(HttpMethod.Post, $"/api/files/{initiated.FileId}/complete", token);
        var firstResponse = await _client.SendAsync(first);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        using var second = AuthenticatedRequest(HttpMethod.Post, $"/api/files/{initiated.FileId}/complete", token);
        var secondResponse = await _client.SendAsync(second);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }
}
