using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Platform;

namespace FileSharing.Mobile.Core.ViewModels;

/// <summary>
/// Backs "Esqueci minha senha" — deliberately shows the exact same success state whether or not
/// the email belongs to an account (see FileSharingApiClient.ForgotPasswordAsync's own remarks):
/// this ViewModel never has a code path that could reveal that distinction to the user.
/// </summary>
public partial class ForgotPasswordViewModel : ObservableObject
{
    private readonly FileSharingApiClient _apiClient;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private string email = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool requestSent;

    public ForgotPasswordViewModel(FileSharingApiClient apiClient, INavigationService navigation)
    {
        _apiClient = apiClient;
        _navigation = navigation;
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsBusy)
            return;

        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = "Informe seu email.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _apiClient.ForgotPasswordAsync(Email);
            if (!result.IsSuccess)
            {
                ErrorMessage = result.Message;
                return;
            }

            RequestSent = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task GoToLoginAsync() => _navigation.GoBackAsync();

    /// <summary>
    /// This app has no registered deep link to open automatically from the emailed reset link
    /// (see docs/mobile.md) — a user who already has their token (copied from the email) reaches
    /// the paste-it-by-hand entry on ResetPasswordPage from here instead.
    /// </summary>
    [RelayCommand]
    private Task GoToResetPasswordAsync() => _navigation.GoToAsync("resetpassword");
}
