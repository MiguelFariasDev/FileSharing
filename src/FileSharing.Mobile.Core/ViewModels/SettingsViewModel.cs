using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Core.Services.Platform;

namespace FileSharing.Mobile.Core.ViewModels;

/// <summary>
/// Backs the Configurações screen. Deliberately shows only settings with real behavior behind
/// them (Fase de navegação §29) — "Tema" is a fixed, honest label ("Claro"), not a toggle,
/// because this app has no dark theme implemented; adding a switch that does nothing would be
/// exactly the kind of fake control the spec asks to avoid.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly LogoutService _logoutService;

    public string AppVersion { get; }

    public SettingsViewModel(IAppInfoService appInfo, LogoutService logoutService)
    {
        _logoutService = logoutService;
        AppVersion = appInfo.VersionString;
    }

    [RelayCommand]
    private Task LogoutAsync() => _logoutService.LogoutAsync();
}
