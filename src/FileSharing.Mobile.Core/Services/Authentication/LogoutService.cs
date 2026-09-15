using FileSharing.Mobile.Core.Services.Activity;
using FileSharing.Mobile.Core.Services.Platform;
using FileSharing.Mobile.Core.Services.SignalR;
using FileSharing.Mobile.Core.ViewModels;

namespace FileSharing.Mobile.Core.Services.Authentication;

/// <summary>
/// The single place logout happens — both ProfileViewModel and SettingsViewModel call this
/// rather than each reimplementing the order (kept identical to the pre-existing
/// HomeViewModel.LogoutAsync this replaced): disconnect SignalR, clear the session, clear
/// every singleton ViewModel's in-memory state, then replace the whole navigation stack with
/// Login so "back" can never reach an authenticated screen again.
/// </summary>
public class LogoutService
{
    private readonly AuthSession _authSession;
    private readonly INotificationService _notificationService;
    private readonly FilesViewModel _filesViewModel;
    private readonly ActivityFeedService _activityFeed;
    private readonly INavigationService _navigation;

    public LogoutService(
        AuthSession authSession,
        INotificationService notificationService,
        FilesViewModel filesViewModel,
        ActivityFeedService activityFeed,
        INavigationService navigation)
    {
        _authSession = authSession;
        _notificationService = notificationService;
        _filesViewModel = filesViewModel;
        _activityFeed = activityFeed;
        _navigation = navigation;
    }

    public async Task LogoutAsync()
    {
        await _notificationService.StopAsync();
        _authSession.ClearSession();
        _filesViewModel.Reset();
        _activityFeed.Clear();
        await _navigation.GoToRootAsync("//login");
    }
}
