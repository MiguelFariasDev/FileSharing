using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Core.Services.Platform;

namespace FileSharing.Mobile.Core.ViewModels;

/// <summary>
/// Backs the Perfil screen (reached from the Home header's avatar, per Fase de navegação §20 —
/// never a bottom-nav tab of its own). Logout lives here now, not on Home — see LogoutService.
/// </summary>
public partial class ProfileViewModel : ObservableObject
{
    private readonly AuthSession _authSession;
    private readonly LogoutService _logoutService;
    private readonly INavigationService _navigation;

    public string? UserEmail => _authSession.CurrentUser?.Email;

    public ProfileViewModel(AuthSession authSession, LogoutService logoutService, INavigationService navigation)
    {
        _authSession = authSession;
        _logoutService = logoutService;
        _navigation = navigation;
    }

    [RelayCommand]
    private Task GoToSettingsAsync() => _navigation.GoToAsync("settings");

    [RelayCommand]
    private Task LogoutAsync() => _logoutService.LogoutAsync();
}
