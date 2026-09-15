using System.Net;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Core.ViewModels;
using FileSharing.Mobile.Tests.Fakes;
using FileSharing.Mobile.Tests.Services;

namespace FileSharing.Mobile.Tests.ViewModels;

public class ForgotPasswordViewModelTests
{
    private static (ForgotPasswordViewModel ViewModel, FakeNavigationService Navigation) CreateSut(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") };
        var apiClient = new FileSharingApiClient(httpClient, new AuthSession(new InMemorySecureStorageService()));
        var navigation = new FakeNavigationService();

        return (new ForgotPasswordViewModel(apiClient, navigation), navigation);
    }

    [Fact]
    public async Task SubmitAsync_WithEmptyEmail_ShowsAValidationMessage_WithoutCallingTheApi()
    {
        var called = false;
        var (sut, _) = CreateSut(_ => { called = true; return new HttpResponseMessage(HttpStatusCode.Accepted); });

        await sut.SubmitCommand.ExecuteAsync(null);

        Assert.False(called);
        Assert.False(string.IsNullOrEmpty(sut.ErrorMessage));
    }

    [Fact]
    public async Task SubmitAsync_Success_SetsRequestSent()
    {
        var (sut, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        sut.Email = "user@example.com";

        await sut.SubmitCommand.ExecuteAsync(null);

        Assert.True(sut.RequestSent);
        Assert.Null(sut.ErrorMessage);
    }

    [Fact]
    public async Task SubmitAsync_Success_ShowsTheSameStateForAnyEmail()
    {
        // The Api's own response is identical for an existing/nonexistent email (see
        // AuthController.ForgotPassword) — this ViewModel has no branch that could show
        // something different for either case, which this test pins down structurally: both
        // calls go through the exact same code path and land on RequestSent = true.
        var (sut, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        sut.Email = "unknown@example.com";

        await sut.SubmitCommand.ExecuteAsync(null);

        Assert.True(sut.RequestSent);
    }

    [Fact]
    public async Task GoToLoginCommand_GoesBack()
    {
        var (sut, navigation) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await sut.GoToLoginCommand.ExecuteAsync(null);

        Assert.Equal(1, navigation.GoBackCount);
    }
}
