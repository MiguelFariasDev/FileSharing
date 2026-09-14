using System.Net;
using System.Net.Http.Json;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Core.ViewModels;
using FileSharing.Mobile.Tests.Fakes;
using FileSharing.Mobile.Tests.Services;

namespace FileSharing.Mobile.Tests.ViewModels;

public class LoginViewModelTests
{
    private static (LoginViewModel ViewModel, AuthSession Session, FakeNavigationService Navigation, FakeNotificationService Notifications) CreateSut(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") };
        var session = new AuthSession(new InMemorySecureStorageService());
        var apiClient = new FileSharingApiClient(httpClient, session);
        var navigation = new FakeNavigationService();
        var notifications = new FakeNotificationService();

        var viewModel = new LoginViewModel(apiClient, session, notifications, navigation);
        return (viewModel, session, navigation, notifications);
    }

    [Fact]
    public async Task LoginAsync_WithEmptyFields_ShowsAValidationMessage_WithoutCallingTheApi()
    {
        var called = false;
        var (sut, _, _, _) = CreateSut(_ => { called = true; return new HttpResponseMessage(HttpStatusCode.OK); });

        await sut.LoginCommand.ExecuteAsync(null);

        Assert.False(called);
        Assert.False(string.IsNullOrEmpty(sut.ErrorMessage));
    }

    [Fact]
    public async Task LoginAsync_Success_EstablishesTheSession_StartsNotifications_AndNavigatesHome()
    {
        var user = new UserResponse(Guid.NewGuid(), "user@example.com");
        var (sut, session, navigation, notifications) = CreateSut(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/login")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new AuthResponse("jwt", DateTimeOffset.UtcNow.AddHours(1))) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(user) });

        sut.Email = "user@example.com";
        sut.Password = "password123";

        await sut.LoginCommand.ExecuteAsync(null);

        Assert.True(session.IsAuthenticated);
        Assert.Equal("//home", navigation.RootRoute);
        Assert.Equal(1, notifications.StartCount);
        // The password field is cleared once no longer needed — never left sitting in a bound
        // property longer than necessary.
        Assert.Equal(string.Empty, sut.Password);
    }

    [Fact]
    public async Task LoginAsync_InvalidCredentials_ShowsAGenericMessage_NeverNavigates()
    {
        var (sut, session, navigation, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        sut.Email = "user@example.com";
        sut.Password = "wrong-password";

        await sut.LoginCommand.ExecuteAsync(null);

        Assert.False(session.IsAuthenticated);
        Assert.Empty(navigation.Visited);
        Assert.Equal("Email ou senha inválidos.", sut.ErrorMessage);
    }

    [Fact]
    public async Task LoginAsync_NeverExposesTheRawApiErrorBody()
    {
        const string internalDetail = "at FileSharing.Api.Internal.Something:line 1";
        var (sut, _, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(internalDetail)
        });

        sut.Email = "user@example.com";
        sut.Password = "password123";

        await sut.LoginCommand.ExecuteAsync(null);

        Assert.DoesNotContain(internalDetail, sut.ErrorMessage);
    }

    [Fact]
    public async Task GoToRegisterCommand_NavigatesToRegister()
    {
        var (sut, _, navigation, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await sut.GoToRegisterCommand.ExecuteAsync(null);

        Assert.Contains("register", navigation.Visited);
    }
}
