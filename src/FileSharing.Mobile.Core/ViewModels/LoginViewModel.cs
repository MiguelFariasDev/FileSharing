using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSharing.Mobile.Core.Models;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Core.Services.Platform;
using FileSharing.Mobile.Core.Services.SignalR;

namespace FileSharing.Mobile.Core.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly FileSharingApiClient _apiClient;
    private readonly AuthSession _authSession;
    private readonly INotificationService _notificationService;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private string email = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    public LoginViewModel(
        FileSharingApiClient apiClient,
        AuthSession authSession,
        INotificationService notificationService,
        INavigationService navigation)
    {
        _apiClient = apiClient;
        _authSession = authSession;
        _notificationService = notificationService;
        _navigation = navigation;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsBusy)
            return;

        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Informe email e senha.";
            return;
        }

        IsBusy = true;
        try
        {
            var loginResult = await _apiClient.LoginAsync(Email, Password);
            if (!loginResult.IsSuccess)
            {
                // Deliberately generic for a credentials failure — mirrors the API's own
                // "Credenciais inválidas." for both "no such user" and "wrong password".
                ErrorMessage = loginResult.ErrorType == ApiErrorType.Unauthorized
                    ? "Email ou senha inválidos."
                    : loginResult.Message;
                return;
            }

            var meResult = await _apiClient.GetMeAsync();
            if (!meResult.IsSuccess)
            {
                ErrorMessage = "Não foi possível concluir o login.";
                return;
            }

            await _authSession.SetSessionAsync(loginResult.Value!, meResult.Value!);
            Password = string.Empty;

            // Not awaited: navigating to Home must never wait on notification hub connectivity.
            _ = _notificationService.StartAsync();

            await _navigation.GoToRootAsync("//home");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task GoToRegisterAsync() => _navigation.GoToAsync("register");
}
