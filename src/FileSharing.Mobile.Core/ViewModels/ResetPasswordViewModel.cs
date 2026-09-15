using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Platform;

namespace FileSharing.Mobile.Core.ViewModels;

/// <summary>
/// Backs the reset-password screen. The token can arrive two ways: via a deep link/route
/// parameter (see ResetPasswordPage's IQueryAttributable — the same mechanism HistoryPage
/// already uses for fileId), auto-validated immediately in <see cref="InitializeAsync"/>; or,
/// since this app has no registered deep link to open from the emailed link (see docs/mobile.md),
/// pasted in by hand — <see cref="NeedsManualToken"/> drives that entry form, and
/// <see cref="ValidateTokenCommand"/> validates it the same way an auto-supplied token is.
/// Either path ends at the same place: the "new password" form only appears once a token has
/// actually been confirmed valid.
/// </summary>
public partial class ResetPasswordViewModel : ObservableObject
{
    private readonly FileSharingApiClient _apiClient;
    private readonly INavigationService _navigation;

    private string _token = string.Empty;

    [ObservableProperty]
    private bool isValidatingToken;

    [ObservableProperty]
    private bool needsManualToken;

    [ObservableProperty]
    private string tokenInput = string.Empty;

    [ObservableProperty]
    private string? tokenErrorMessage;

    [ObservableProperty]
    private string newPassword = string.Empty;

    [ObservableProperty]
    private string confirmPassword = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool resetSucceeded;

    /// <summary>True only in the single state the "new password" form itself should be visible — see ResetPasswordPage.xaml, which has no way to express this exclusivity as a plain binding.</summary>
    public bool ShowForm => !IsValidatingToken && !NeedsManualToken && TokenErrorMessage is null && !ResetSucceeded;

    partial void OnIsValidatingTokenChanged(bool value) => OnPropertyChanged(nameof(ShowForm));
    partial void OnNeedsManualTokenChanged(bool value) => OnPropertyChanged(nameof(ShowForm));
    partial void OnTokenErrorMessageChanged(string? value) => OnPropertyChanged(nameof(ShowForm));
    partial void OnResetSucceededChanged(bool value) => OnPropertyChanged(nameof(ShowForm));

    public ResetPasswordViewModel(FileSharingApiClient apiClient, INavigationService navigation)
    {
        _apiClient = apiClient;
        _navigation = navigation;
    }

    /// <summary>Called once, right after navigation — see ResetPasswordPage.ApplyQueryAttributes. A null/empty token (the normal case: opening this screen from within the app, not from a link) switches to manual entry instead of failing outright.</summary>
    public async Task InitializeAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            NeedsManualToken = true;
            return;
        }

        await ValidateAsync(token);
    }

    [RelayCommand]
    private Task ValidateTokenAsync() => ValidateAsync(TokenInput.Trim());

    private async Task ValidateAsync(string token)
    {
        _token = token;
        NeedsManualToken = false;
        TokenErrorMessage = null;

        if (string.IsNullOrWhiteSpace(_token))
        {
            TokenErrorMessage = "Informe o token recebido por email.";
            NeedsManualToken = true;
            return;
        }

        IsValidatingToken = true;
        try
        {
            var result = await _apiClient.ValidateResetTokenAsync(_token);
            if (!result.IsSuccess)
                TokenErrorMessage = MessageForCode(result.Code, result.Message!);
        }
        finally
        {
            IsValidatingToken = false;
        }
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsBusy)
            return;

        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword.Length < 8)
        {
            ErrorMessage = "A senha deve ter pelo menos 8 caracteres.";
            return;
        }

        if (NewPassword != ConfirmPassword)
        {
            ErrorMessage = "As senhas não coincidem.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _apiClient.ResetPasswordAsync(_token, NewPassword);
            if (!result.IsSuccess)
            {
                // A token can expire/be used from another tab between page load and submit —
                // that case is shown the same way the initial validation failure is, not as a
                // generic form error.
                if (result.Code is "AUTH_PASSWORD_RESET_EXPIRED" or "AUTH_PASSWORD_RESET_USED" or "AUTH_PASSWORD_RESET_INVALID")
                    TokenErrorMessage = MessageForCode(result.Code, result.Message!);
                else
                    ErrorMessage = result.Message;

                return;
            }

            ResetSucceeded = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task GoToLoginAsync() => _navigation.GoToRootAsync("//login");

    [RelayCommand]
    private Task GoToForgotPasswordAsync() => _navigation.GoToAsync("forgotpassword");

    // Client-owned copy, never the raw server message — see docs/api-errors.md: the Mobile/Web
    // contract is the "code", not whatever Portuguese sentence the Api happens to send today.
    private static string MessageForCode(string? code, string fallback) => code switch
    {
        "AUTH_PASSWORD_RESET_EXPIRED" => "Este link de recuperação expirou. Solicite uma nova recuperação de senha.",
        "AUTH_PASSWORD_RESET_USED" => "Este link de recuperação já foi utilizado. Solicite uma nova recuperação de senha, se necessário.",
        "AUTH_PASSWORD_RESET_INVALID" => "Este código de recuperação é inválido.",
        _ => fallback
    };
}
