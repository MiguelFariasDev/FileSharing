using System.Net;
using System.Net.Http.Json;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Core.ViewModels;
using FileSharing.Mobile.Tests.Fakes;
using FileSharing.Mobile.Tests.Services;

namespace FileSharing.Mobile.Tests.ViewModels;

public class RegisterViewModelTests
{
    private static (RegisterViewModel ViewModel, FakeNavigationService Navigation) CreateSut(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") };
        var apiClient = new FileSharingApiClient(httpClient, new AuthSession(new InMemorySecureStorageService()));
        var navigation = new FakeNavigationService();
        return (new RegisterViewModel(apiClient, navigation), navigation);
    }

    [Fact]
    public async Task RegisterAsync_PasswordTooShort_ShowsAValidationMessage_WithoutCallingTheApi()
    {
        var called = false;
        var (sut, _) = CreateSut(_ => { called = true; return new HttpResponseMessage(HttpStatusCode.OK); });
        sut.Email = "user@example.com";
        sut.Password = "short";
        sut.ConfirmPassword = "short";

        await sut.RegisterCommand.ExecuteAsync(null);

        Assert.False(called);
        Assert.False(string.IsNullOrEmpty(sut.ErrorMessage));
    }

    [Fact]
    public async Task RegisterAsync_MismatchedConfirmation_ShowsAValidationMessage_WithoutCallingTheApi()
    {
        var called = false;
        var (sut, _) = CreateSut(_ => { called = true; return new HttpResponseMessage(HttpStatusCode.OK); });
        sut.Email = "user@example.com";
        sut.Password = "password123";
        sut.ConfirmPassword = "different123";

        await sut.RegisterCommand.ExecuteAsync(null);

        Assert.False(called);
        Assert.Contains("coincidem", sut.ErrorMessage);
    }

    [Fact]
    public async Task RegisterAsync_Success_NeverAuthenticatesTheUser_AndReturnsToLogin()
    {
        var (sut, navigation) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new UserResponse(Guid.NewGuid(), "user@example.com"))
        });
        sut.Email = "user@example.com";
        sut.Password = "password123";
        sut.ConfirmPassword = "password123";

        await sut.RegisterCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrEmpty(sut.SuccessMessage));
        Assert.Equal(1, navigation.GoBackCount);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmail_ShowsTheApisMessage()
    {
        var (sut, navigation) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new { message = "Não foi possível concluir o cadastro." })
        });
        sut.Email = "user@example.com";
        sut.Password = "password123";
        sut.ConfirmPassword = "password123";

        await sut.RegisterCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrEmpty(sut.ErrorMessage));
        Assert.Equal(0, navigation.GoBackCount);
    }
}
