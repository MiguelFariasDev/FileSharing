using System.Net;
using System.Net.Http.Json;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Application.DTOs.Files;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Activity;
using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Core.ViewModels;
using FileSharing.Mobile.Tests.Fakes;
using FileSharing.Mobile.Tests.Services;

namespace FileSharing.Mobile.Tests.ViewModels;

public class ProfileViewModelTests
{
    private static (ProfileViewModel ViewModel, FakeNotificationService Notifications, FakeNavigationService Navigation, AuthSession Session, FilesViewModel Files) CreateSut()
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
        var viewModel = new ProfileViewModel(session, logoutService, navigation);

        return (viewModel, notifications, navigation, session, filesViewModel);
    }

    [Fact]
    public async Task UserEmail_ReflectsTheCurrentSession()
    {
        var (sut, _, _, session, _) = CreateSut();
        await session.SetSessionAsync(new AuthResponse("jwt", DateTimeOffset.UtcNow.AddHours(1)), new UserResponse(Guid.NewGuid(), "owner@example.com"));

        Assert.Equal("owner@example.com", sut.UserEmail);
    }

    [Fact]
    public async Task GoToSettingsCommand_NavigatesToSettings()
    {
        var (sut, _, navigation, _, _) = CreateSut();

        await sut.GoToSettingsCommand.ExecuteAsync(null);

        Assert.Contains("settings", navigation.Visited);
    }

    [Fact]
    public async Task LogoutCommand_StopsNotifications_ClearsSession_ClearsFiles_AndNavigatesToLogin()
    {
        var (sut, notifications, navigation, session, files) = CreateSut();
        await session.SetSessionAsync(new AuthResponse("jwt", DateTimeOffset.UtcNow.AddHours(1)), new UserResponse(Guid.NewGuid(), "a@b.com"));
        await files.LoadCommand.ExecuteAsync(null);

        await sut.LogoutCommand.ExecuteAsync(null);

        Assert.Equal(1, notifications.StopCount);
        Assert.False(session.IsAuthenticated);
        Assert.Empty(files.Files);
        Assert.Equal("//login", navigation.RootRoute);
    }
}
