using System.Net;
using System.Net.Http.Json;
using FileSharing.Application.DTOs.Files;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Activity;
using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Core.ViewModels;
using FileSharing.Mobile.Tests.Fakes;
using FileSharing.Mobile.Tests.Services;

namespace FileSharing.Mobile.Tests.ViewModels;

public class SettingsViewModelTests
{
    private static (SettingsViewModel ViewModel, FakeNotificationService Notifications, FakeNavigationService Navigation, AuthSession Session) CreateSut()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<FileSummaryResponse>())
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") };
        var session = new AuthSession(new InMemorySecureStorageService());
        var apiClient = new FileSharingApiClient(httpClient, session);
        var notifications = new FakeNotificationService();
        var navigation = new FakeNavigationService();
        var dispatcher = new ImmediateMainThreadDispatcher();

        var filesViewModel = new FilesViewModel(apiClient, notifications, navigation, dispatcher);
        var activityFeed = new ActivityFeedService(notifications, dispatcher);
        var logoutService = new LogoutService(session, notifications, filesViewModel, activityFeed, navigation);
        var appInfo = new FakeAppInfoService { VersionString = "2.3.4" };
        var viewModel = new SettingsViewModel(appInfo, logoutService);

        return (viewModel, notifications, navigation, session);
    }

    [Fact]
    public void AppVersion_ComesFromTheAppInfoService()
    {
        var (sut, _, _, _) = CreateSut();

        Assert.Equal("2.3.4", sut.AppVersion);
    }

    [Fact]
    public async Task LogoutCommand_StopsNotifications_ClearsSession_AndNavigatesToLogin()
    {
        var (sut, notifications, navigation, session) = CreateSut();

        await sut.LogoutCommand.ExecuteAsync(null);

        Assert.Equal(1, notifications.StopCount);
        Assert.False(session.IsAuthenticated);
        Assert.Equal("//login", navigation.RootRoute);
    }
}
