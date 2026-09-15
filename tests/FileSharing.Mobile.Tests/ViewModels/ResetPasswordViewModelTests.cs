using System.Net;
using System.Net.Http.Json;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Core.ViewModels;
using FileSharing.Mobile.Tests.Fakes;
using FileSharing.Mobile.Tests.Services;

namespace FileSharing.Mobile.Tests.ViewModels;

public class ResetPasswordViewModelTests
{
    private static (ResetPasswordViewModel ViewModel, FakeNavigationService Navigation, FakeHttpMessageHandler Handler) CreateSut(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") };
        var apiClient = new FileSharingApiClient(httpClient, new AuthSession(new InMemorySecureStorageService()));
        var navigation = new FakeNavigationService();

        return (new ResetPasswordViewModel(apiClient, navigation), navigation, handler);
    }

    private static HttpResponseMessage ProblemResponse(HttpStatusCode status, string code, string title) => new(status)
    {
        Content = JsonContent.Create(new { title, status = (int)status, code })
    };

    [Fact]
    public async Task InitializeAsync_WithNoToken_SwitchesToManualEntry_WithoutCallingTheApi()
    {
        var (sut, _, handler) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await sut.InitializeAsync(null);

        Assert.Empty(handler.Requests);
        Assert.False(sut.IsValidatingToken);
        Assert.False(sut.ShowForm);
        Assert.True(sut.NeedsManualToken);
        Assert.Null(sut.TokenErrorMessage);
    }

    [Fact]
    public async Task ValidateTokenCommand_WithAPastedValidToken_ShowsTheForm()
    {
        var (sut, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK));
        await sut.InitializeAsync(null);

        sut.TokenInput = "pasted-token";
        await sut.ValidateTokenCommand.ExecuteAsync(null);

        Assert.False(sut.NeedsManualToken);
        Assert.Null(sut.TokenErrorMessage);
        Assert.True(sut.ShowForm);
    }

    [Fact]
    public async Task ValidateTokenCommand_WithAnEmptyPastedToken_AsksForOneAgain()
    {
        var called = false;
        var (sut, _, _) = CreateSut(_ => { called = true; return new HttpResponseMessage(HttpStatusCode.OK); });
        await sut.InitializeAsync(null);

        sut.TokenInput = "   ";
        await sut.ValidateTokenCommand.ExecuteAsync(null);

        Assert.False(called);
        Assert.True(sut.NeedsManualToken);
        Assert.NotNull(sut.TokenErrorMessage);
    }

    [Fact]
    public async Task InitializeAsync_WithAValidToken_ShowsTheForm()
    {
        var (sut, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await sut.InitializeAsync("valid-token");

        Assert.False(sut.IsValidatingToken);
        Assert.Null(sut.TokenErrorMessage);
        Assert.True(sut.ShowForm);
    }

    [Theory]
    [InlineData("AUTH_PASSWORD_RESET_EXPIRED")]
    [InlineData("AUTH_PASSWORD_RESET_USED")]
    [InlineData("AUTH_PASSWORD_RESET_INVALID")]
    public async Task InitializeAsync_WithAnInvalidToken_ShowsTheTokenErrorState_NotTheForm(string code)
    {
        var (sut, _, _) = CreateSut(_ => ProblemResponse(HttpStatusCode.Gone, code, "some message"));

        await sut.InitializeAsync("some-token");

        Assert.False(sut.ShowForm);
        Assert.NotNull(sut.TokenErrorMessage);
    }

    [Fact]
    public async Task SubmitAsync_WithMismatchedPasswords_DoesNotCallTheResetEndpoint()
    {
        var (sut, _, handler) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK));
        await sut.InitializeAsync("valid-token");
        var requestsBeforeSubmit = handler.Requests.Count;

        sut.NewPassword = "Password123";
        sut.ConfirmPassword = "Different123";
        await sut.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(requestsBeforeSubmit, handler.Requests.Count);
        Assert.Equal("As senhas não coincidem.", sut.ErrorMessage);
    }

    [Fact]
    public async Task SubmitAsync_Success_SetsResetSucceeded()
    {
        var (sut, _, _) = CreateSut(request =>
            request.Method == HttpMethod.Get
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.OK));
        await sut.InitializeAsync("valid-token");

        sut.NewPassword = "NewPassword123";
        sut.ConfirmPassword = "NewPassword123";
        await sut.SubmitCommand.ExecuteAsync(null);

        Assert.True(sut.ResetSucceeded);
        Assert.False(sut.ShowForm);
    }

    [Fact]
    public async Task SubmitAsync_WithATokenThatExpiredMeanwhile_ShowsTheTokenErrorState()
    {
        var (sut, _, _) = CreateSut(request =>
            request.Method == HttpMethod.Get
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : ProblemResponse(HttpStatusCode.Gone, "AUTH_PASSWORD_RESET_EXPIRED", "expired"));
        await sut.InitializeAsync("valid-token");

        sut.NewPassword = "NewPassword123";
        sut.ConfirmPassword = "NewPassword123";
        await sut.SubmitCommand.ExecuteAsync(null);

        Assert.False(sut.ResetSucceeded);
        Assert.False(sut.ShowForm);
        Assert.NotNull(sut.TokenErrorMessage);
    }

    [Fact]
    public async Task GoToLoginCommand_NavigatesToLoginRoot()
    {
        var (sut, navigation, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await sut.GoToLoginCommand.ExecuteAsync(null);

        Assert.Equal("//login", navigation.RootRoute);
    }
}
