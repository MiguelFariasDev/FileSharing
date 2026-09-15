using System.Net;
using System.Net.Http.Json;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Mobile.Core.Models;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Tests.Services;

namespace FileSharing.Mobile.Tests.Services.ApiClient;

public class FileSharingApiClientTests
{
    private static (FileSharingApiClient Client, FakeHttpMessageHandler Handler, AuthSession Session) CreateSut(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") };
        var session = new AuthSession(new InMemorySecureStorageService());
        return (new FileSharingApiClient(httpClient, session), handler, session);
    }

    [Fact]
    public async Task LoginAsync_Success_ReturnsTheAuthResponse()
    {
        var auth = new AuthResponse("jwt-token", DateTimeOffset.UtcNow.AddHours(1));
        var (sut, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(auth) });

        var result = await sut.LoginAsync("user@example.com", "password");

        Assert.True(result.IsSuccess);
        Assert.Equal("jwt-token", result.Value!.AccessToken);
    }

    [Fact]
    public async Task LoginAsync_NeverAttachesAnAuthorizationHeader()
    {
        var (sut, handler, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await sut.LoginAsync("user@example.com", "wrong-password");

        Assert.All(handler.Requests, r => Assert.Null(r.Headers.Authorization));
    }

    [Fact]
    public async Task LoginAsync_InvalidCredentials_MapsToUnauthorized_WithAFriendlyMessage()
    {
        var (sut, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await sut.LoginAsync("user@example.com", "wrong-password");

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorType.Unauthorized, result.ErrorType);
        Assert.DoesNotContain("Exception", result.Message);
    }

    [Fact]
    public async Task GetMyFilesAsync_AttachesTheSessionsAccessToken()
    {
        var (sut, handler, session) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(Array.Empty<Application.DTOs.Files.FileSummaryResponse>())
        });
        await session.SetSessionAsync(new AuthResponse("the-jwt", DateTimeOffset.UtcNow.AddHours(1)), new UserResponse(Guid.NewGuid(), "a@b.com"));

        await sut.GetMyFilesAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("the-jwt", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task GetMyFilesAsync_WithoutASession_SendsNoAuthorizationHeader()
    {
        var (sut, handler, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await sut.GetMyFilesAsync();

        Assert.Null(handler.Requests.Single().Headers.Authorization);
    }

    [Fact]
    public async Task AnyAuthenticatedCall_On401_RaisesSessionExpired()
    {
        var (sut, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var raised = false;
        sut.SessionExpired += () => raised = true;

        await sut.GetMyFilesAsync();

        Assert.True(raised);
    }

    [Fact]
    public async Task SuccessfulCall_NeverRaisesSessionExpired()
    {
        var (sut, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(Array.Empty<Application.DTOs.Files.FileSummaryResponse>())
        });
        var raised = false;
        sut.SessionExpired += () => raised = true;

        await sut.GetMyFilesAsync();

        Assert.False(raised);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ApiErrorType.NotFound)]
    [InlineData(HttpStatusCode.Conflict, ApiErrorType.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests, ApiErrorType.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError, ApiErrorType.ServerError)]
    public async Task ErrorStatusCodes_AreMappedToTheExpectedApiErrorType(HttpStatusCode statusCode, ApiErrorType expected)
    {
        var (sut, _, _) = CreateSut(_ => new HttpResponseMessage(statusCode));

        var result = await sut.GetMyFilesAsync();

        Assert.Equal(expected, result.ErrorType);
    }

    [Fact]
    public async Task NetworkFailure_MapsToANetworkError_WithAFriendlyMessage_NeverAnException()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("Name or service not known"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") };
        var sut = new FileSharingApiClient(httpClient, new AuthSession(new InMemorySecureStorageService()));

        var result = await sut.GetMyFilesAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorType.Network, result.ErrorType);
        Assert.DoesNotContain("HttpRequestException", result.Message);
    }

    [Fact]
    public async Task ErrorMessages_AreNeverTheRawResponseBody()
    {
        const string internalDetail = "System.InvalidOperationException at FileSharing.Api.Internal:line 42";
        var (sut, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(internalDetail)
        });

        var result = await sut.GetMyFilesAsync();

        Assert.DoesNotContain(internalDetail, result.Message);
    }

    [Fact]
    public async Task ErrorResponse_WithACodeAndTitleInTheBody_SurfacesBothOnApiResult()
    {
        var (sut, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.Gone)
        {
            Content = JsonContent.Create(new { title = "Este link de recuperação expirou.", status = 410, code = "AUTH_PASSWORD_RESET_EXPIRED" })
        });

        var result = await sut.ValidateResetTokenAsync("some-token");

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorType.Gone, result.ErrorType);
        Assert.Equal("AUTH_PASSWORD_RESET_EXPIRED", result.Code);
        Assert.Equal("Este link de recuperação expirou.", result.Message);
    }

    [Fact]
    public async Task ForgotPasswordAsync_OnSuccess_ReturnsSuccessRegardlessOfBody()
    {
        var (sut, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = JsonContent.Create(new { message = "Se a conta existir, enviaremos instruções para redefinir sua senha." })
        });

        var result = await sut.ForgotPasswordAsync("user@example.com");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ForgotPasswordAsync_NeverAttachesAnAuthorizationHeader()
    {
        var (sut, handler, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.Accepted));

        await sut.ForgotPasswordAsync("user@example.com");

        Assert.All(handler.Requests, r => Assert.Null(r.Headers.Authorization));
    }

    [Fact]
    public async Task ResetPasswordAsync_WithUsedTokenCode_SurfacesTheCode()
    {
        var (sut, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.Gone)
        {
            Content = JsonContent.Create(new { title = "Este link de recuperação já foi utilizado.", status = 410, code = "AUTH_PASSWORD_RESET_USED" })
        });

        var result = await sut.ResetPasswordAsync("some-token", "NewPassword123");

        Assert.False(result.IsSuccess);
        Assert.Equal("AUTH_PASSWORD_RESET_USED", result.Code);
    }
}
