using System.Net;
using System.Net.Http.Json;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Application.DTOs.Files;
using FileSharing.Application.DTOs.Notifications;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Activity;
using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Core.ViewModels;
using FileSharing.Mobile.Tests.Fakes;
using FileSharing.Mobile.Tests.Services;

namespace FileSharing.Mobile.Tests.ViewModels;

public class HomeViewModelTests
{
    private static (HomeViewModel ViewModel, FilesViewModel Files, FakeNotificationService Notifications, FakeNavigationService Navigation, AuthSession Session) CreateSut(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") };
        var session = new AuthSession(new InMemorySecureStorageService());
        var apiClient = new FileSharingApiClient(httpClient, session);
        var notifications = new FakeNotificationService();
        var navigation = new FakeNavigationService();
        var dispatcher = new ImmediateMainThreadDispatcher();

        var filesViewModel = new FilesViewModel(apiClient, notifications, navigation, dispatcher);
        var activityFeed = new ActivityFeedService(notifications, dispatcher);
        var viewModel = new HomeViewModel(filesViewModel, activityFeed, session, navigation);
        return (viewModel, filesViewModel, notifications, navigation, session);
    }

    private static FileSummaryResponse ActiveFile(Guid? id = null, DateTimeOffset? expiresAt = null) => new(
        id ?? Guid.NewGuid(), "document.pdf", "application/pdf", 2048, false, "Active",
        DateTimeOffset.UtcNow.AddHours(-1), expiresAt ?? DateTimeOffset.UtcNow.AddHours(23), 0, true);

    [Fact]
    public async Task EnsureLoadedAsync_PopulatesFilesAndSummaryCounts()
    {
        var (sut, files, _, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<FileSummaryResponse> { ActiveFile() })
        });

        await sut.EnsureLoadedAsync();

        Assert.Single(files.Files);
        Assert.Equal(1, sut.ActiveCount);
        Assert.False(sut.IsLoading);
    }

    [Fact]
    public async Task ExpiringSoonCount_CountsOnlyFilesExpiringWithinTwoHours()
    {
        var soonId = Guid.NewGuid();
        var laterId = Guid.NewGuid();
        var (sut, _, _, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<FileSummaryResponse>
            {
                ActiveFile(soonId, DateTimeOffset.UtcNow.AddMinutes(30)),
                ActiveFile(laterId, DateTimeOffset.UtcNow.AddHours(20))
            })
        });

        await sut.EnsureLoadedAsync();

        Assert.Equal(1, sut.ExpiringSoonCount);
    }

    [Fact]
    public async Task RecentFiles_ReturnsAtMostThreeFiles()
    {
        var (sut, _, _, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(Enumerable.Range(0, 5).Select(_ => ActiveFile()).ToList())
        });

        await sut.EnsureLoadedAsync();

        Assert.Equal(3, sut.RecentFiles.Count());
    }

    [Fact]
    public async Task FileDownloadedEvent_IncrementsRecentDownloadsCount_AndSetsLastActivityLabel()
    {
        var fileId = Guid.NewGuid();
        var (sut, _, notifications, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<FileSummaryResponse> { ActiveFile(fileId) })
        });
        await sut.EnsureLoadedAsync();

        notifications.RaiseFileDownloaded(new FileDownloadedNotification(fileId, "document.pdf", DateTimeOffset.UtcNow));

        Assert.Equal(1, sut.RecentDownloadsCount);
        Assert.Contains("document.pdf", sut.LastActivityLabel);
    }

    [Fact]
    public async Task EnsureLoadedAsync_ApiFailure_ReportsError()
    {
        var (sut, _, _, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await sut.EnsureLoadedAsync();

        Assert.True(sut.HasError);
        Assert.False(sut.IsLoading);
    }

    [Fact]
    public async Task GoToUploadCommand_NavigatesToTheUploadTab()
    {
        var (sut, _, _, navigation, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<FileSummaryResponse>())
        });

        await sut.GoToUploadCommand.ExecuteAsync(null);

        Assert.Contains("//upload", navigation.Visited);
    }

    [Fact]
    public async Task GoToProfileCommand_NavigatesToProfile()
    {
        var (sut, _, _, navigation, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<FileSummaryResponse>())
        });

        await sut.GoToProfileCommand.ExecuteAsync(null);

        Assert.Contains("profile", navigation.Visited);
    }

    [Fact]
    public async Task UserEmail_ReflectsTheCurrentSession()
    {
        var (sut, _, _, _, session) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK));
        await session.SetSessionAsync(new AuthResponse("jwt", DateTimeOffset.UtcNow.AddHours(1)), new UserResponse(Guid.NewGuid(), "a@b.com"));

        Assert.Equal("a@b.com", sut.UserEmail);
    }
}
