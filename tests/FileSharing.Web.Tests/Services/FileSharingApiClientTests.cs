using System.Net;
using System.Net.Http.Json;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Web.Models;
using FileSharing.Web.Services;

namespace FileSharing.Web.Tests.Services;

public class FileSharingApiClientTests
{
    private static (FileSharingApiClient Client, FakeHttpMessageHandler Handler, AuthTokenProvider TokenProvider) CreateSut(
        HttpStatusCode statusCode,
        HttpContent? content = null)
    {
        var handler = new FakeHttpMessageHandler(statusCode, content);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        var tokenProvider = new AuthTokenProvider();
        var client = new FileSharingApiClient(httpClient, tokenProvider);
        return (client, handler, tokenProvider);
    }

    [Fact]
    public async Task LoginAsync_NeverAttachesAnAuthorizationHeader()
    {
        var (client, handler, _) = CreateSut(HttpStatusCode.OK, JsonContent.Create(new AuthResponse("token", DateTimeOffset.UtcNow.AddHours(1))));

        await client.LoginAsync("user@example.com", "password");

        Assert.Null(handler.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task LoginAsync_Success_ReturnsTheAuthResponse()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var (client, _, _) = CreateSut(HttpStatusCode.OK, JsonContent.Create(new AuthResponse("jwt-token", expiresAt)));

        var result = await client.LoginAsync("user@example.com", "password");

        Assert.True(result.IsSuccess);
        Assert.Equal("jwt-token", result.Value!.AccessToken);
    }

    [Fact]
    public async Task LoginAsync_InvalidCredentials_MapsTo401_WithAFriendlyMessage()
    {
        var (client, _, _) = CreateSut(HttpStatusCode.Unauthorized);

        var result = await client.LoginAsync("user@example.com", "wrong-password");

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorType.Unauthorized, result.ErrorType);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task GetMyFilesAsync_AttachesAuthorizationHeader_WhenATokenIsPresent()
    {
        var (client, handler, tokenProvider) = CreateSut(HttpStatusCode.OK, JsonContent.Create(Array.Empty<FileSharing.Application.DTOs.Files.FileSummaryResponse>()));
        tokenProvider.SetToken("my-jwt", DateTimeOffset.UtcNow.AddHours(1));

        await client.GetMyFilesAsync();

        Assert.NotNull(handler.LastRequest!.Headers.Authorization);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("my-jwt", handler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task GetMyFilesAsync_WithoutAToken_SendsNoAuthorizationHeader()
    {
        var (client, handler, _) = CreateSut(HttpStatusCode.OK, JsonContent.Create(Array.Empty<FileSharing.Application.DTOs.Files.FileSummaryResponse>()));

        await client.GetMyFilesAsync();

        Assert.Null(handler.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task AnyCall_On401_RaisesSessionExpired()
    {
        var (client, _, _) = CreateSut(HttpStatusCode.Unauthorized);
        var raised = false;
        client.SessionExpired += () => raised = true;

        await client.GetMyFilesAsync();

        Assert.True(raised);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ApiErrorType.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, ApiErrorType.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, ApiErrorType.NotFound)]
    [InlineData(HttpStatusCode.Conflict, ApiErrorType.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests, ApiErrorType.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError, ApiErrorType.ServerError)]
    public async Task ErrorStatusCodes_AreMappedToTheExpectedApiErrorType(HttpStatusCode statusCode, ApiErrorType expected)
    {
        var (client, _, _) = CreateSut(statusCode);

        var result = await client.GetMyFilesAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.ErrorType);
    }

    [Fact]
    public async Task ErrorMessages_AreNeverTheRawResponseBody()
    {
        // A ProblemDetails/exception page could contain internal detail (stack trace, SQL,
        // AWS errors) — the user-facing message must never be the raw body.
        var (client, _, _) = CreateSut(HttpStatusCode.InternalServerError, new StringContent("System.Exception: leaked stack trace at SomeInternalMethod()"));

        var result = await client.GetMyFilesAsync();

        Assert.DoesNotContain("System.Exception", result.Message);
        Assert.DoesNotContain("SomeInternalMethod", result.Message);
    }

    [Fact]
    public async Task GenerateLinkAsync_Success_ReturnsThePublicLinkResponse()
    {
        var fileId = Guid.NewGuid();
        var (client, handler, _) = CreateSut(
            HttpStatusCode.OK,
            JsonContent.Create(new PublicLinkResponse(fileId, "new-token", "https://api.test/api/public/files/new-token")));

        var result = await client.GenerateLinkAsync(fileId);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-token", result.Value!.AccessToken);
        Assert.Equal("https://api.test/api/public/files/new-token", result.Value.PublicUrl);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.EndsWith($"/api/files/{fileId}/link", handler.LastRequest.RequestUri!.AbsolutePath);
    }
}
