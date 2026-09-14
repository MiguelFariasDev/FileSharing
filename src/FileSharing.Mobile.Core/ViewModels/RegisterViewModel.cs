using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Platform;

namespace FileSharing.Mobile.Core.ViewModels;

public partial class RegisterViewModel : ObservableObject
{
    private readonly FileSharingApiClient _apiClient;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private string email = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private string confirmPassword = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? successMessage;

    public RegisterViewModel(FileSharingApiClient apiClient, INavigationService navigation)
    {
        _apiClient = apiClient;
        _navigation = navigation;
    }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (IsBusy)
            return;

        ErrorMessage = null;
        SuccessMessage = null;

        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Informe email e senha.";
            return;
        }

        if (Password.Length < 8)
        {
            ErrorMessage = "A senha deve ter pelo menos 8 caracteres.";
            return;
        }

        if (Password != ConfirmPassword)
        {
            ErrorMessage = "As senhas não coincidem.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _apiClient.RegisterAsync(Email, Password);
            if (!result.IsSuccess)
            {
                ErrorMessage = result.Message;
                return;
            }

            // Registering does not authenticate the caller (matches the API contract exactly —
            // see AuthService.RegisterAsync/IAuthService) — always send the user to Login next,
            // never assume they're signed in.
            SuccessMessage = "Conta criada com sucesso. Faça login para continuar.";
            Password = string.Empty;
            ConfirmPassword = string.Empty;

            await Task.Delay(TimeSpan.FromSeconds(2));
            await _navigation.GoBackAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task GoToLoginAsync() => _navigation.GoBackAsync();
}
