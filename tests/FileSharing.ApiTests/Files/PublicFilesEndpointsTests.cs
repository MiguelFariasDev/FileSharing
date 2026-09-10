using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FileSharing.Application.Abstractions.Storage;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Application.DTOs.Files;
using FileSharing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FileSharing.ApiTests.Files;

public class PublicFilesEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Password = "SenhaForte123";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PublicFilesEndpointsTests(CustomWebApplicationFactory factory)
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

    private async Task<string> CreatePublicLinkAsync(string ownerAccessToken)
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

        return link.GetProperty("accessToken").GetString()!;
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

    [Fact]
    public async Task GetPublicFile_DoesNotRequireAuthentication()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var accessToken = await CreatePublicLinkAsync(ownerToken);

        // No Authorization header attached at all.
        var response = await _client.GetAsync($"/api/public/files/{accessToken}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetPublicFile_WithValidToken_ReturnsFileInfo()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var accessToken = await CreatePublicLinkAsync(ownerToken);

        var response = await _client.GetAsync($"/api/public/files/{accessToken}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<PublicFileAccessResponse>();
        Assert.NotNull(body);
        Assert.Equal("document.pdf", body!.OriginalFileName);
        Assert.Equal(1024, body.SizeBytes);
        Assert.Equal("application/pdf", body.ContentType);
    }

    [Fact]
    public async Task GetPublicFile_WithUnknownToken_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/public/files/this-token-was-never-issued-abc123");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPublicFile_WithExpiredFilesToken_ReturnsNotFound()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var accessToken = await CreatePublicLinkAsync(ownerToken);
        await ExpireFileWithTokenAsync(accessToken);

        var response = await _client.GetAsync($"/api/public/files/{accessToken}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPublicFile_UnknownAndExpiredTokens_ReturnTheSameGenericBody()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var accessToken = await CreatePublicLinkAsync(ownerToken);
        await ExpireFileWithTokenAsync(accessToken);

        var expiredResponse = await _client.GetAsync($"/api/public/files/{accessToken}");
        var unknownResponse = await _client.GetAsync("/api/public/files/completely-made-up-token-xyz");

        Assert.Equal(expiredResponse.StatusCode, unknownResponse.StatusCode);

        // Compare shape, not the raw body — ASP.NET Core's default 404 ProblemDetails
        // includes a per-request traceId, which differs on every call but reveals nothing
        // about the token and is not the kind of "distinguishing detail" being tested here.
        var expiredBody = await expiredResponse.Content.ReadFromJsonAsync<JsonElement>();
        var unknownBody = await unknownResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expiredBody.GetProperty("status").GetInt32(), unknownBody.GetProperty("status").GetInt32());
        Assert.Equal(expiredBody.GetProperty("title").GetString(), unknownBody.GetProperty("title").GetString());
    }

    [Fact]
    public async Task GetPublicFile_ErrorResponse_DoesNotContainTheRequestedToken()
    {
        const string guessedToken = "super-secret-guessed-token-value";

        var response = await _client.GetAsync($"/api/public/files/{guessedToken}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(guessedToken, body);
    }
}
