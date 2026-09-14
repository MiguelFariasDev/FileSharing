using System.Net;
using System.Net.Http.Json;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Application.DTOs.Files;
using FileSharing.Application.DTOs.Notifications;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Core.ViewModels;
using FileSharing.Mobile.Tests.Fakes;
using FileSharing.Mobile.Tests.Services;

namespace FileSharing.Mobile.Tests.ViewModels;

public class HomeViewModelTests
{
    private static (HomeViewModel ViewModel, FakeNotificationService Notifications, FakeNavigationService Navigation, AuthSession Session) CreateSut(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") };
        var session = new AuthSession(new InMemorySecureStorageService());
        var apiClient = new FileSharingApiClient(httpClient, session);
        var notifications = new FakeNotificationService();
        var navigation = new FakeNavigationService();
        var dispatcher = new ImmediateMainThreadDispatcher();

        var viewModel = new HomeViewModel(apiClient, session, notifications, navigation, dispatcher);
        return (viewModel, notifications, navigation, session);
    }

    private static FileSummaryResponse ActiveFile(Guid? id = null) => new(
        id ?? Guid.NewGuid(), "document.pdf", "application/pdf", 2048, false, "Active",
        DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(23), 0, true);

    [Fact]
    public async Task LoadAsync_Success_PopulatesFiles()
    {
        var fileId = Guid.NewGuid();
        var (sut, _, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<FileSummaryResponse> { ActiveFile(fileId) })
        });

        await sut.LoadCommand.ExecuteAsync(null);

        Assert.Single(sut.Files);
        Assert.Equal(fileId, sut.Files[0].FileId);
        Assert.False(sut.IsLoading);
        Assert.False(sut.HasError);
    }

    [Fact]
    public async Task LoadAsync_NoFiles_ReportsEmpty()
    {
        var (sut, _, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<FileSummaryResponse>())
        });

        await sut.LoadCommand.ExecuteAsync(null);

        Assert.True(sut.IsEmpty);
    }

    [Fact]
    public async Task LoadAsync_ApiFailure_ReportsError_NeverLeavesIsLoadingStuckTrue()
    {
        var (sut, _, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await sut.LoadCommand.ExecuteAsync(null);

        Assert.True(sut.HasError);
        Assert.False(sut.IsLoading);
        Assert.False(sut.IsEmpty); // error state, not empty state — a different message to the user
    }

    [Fact]
    public async Task FileDownloadedEvent_IncrementsTheMatchingFilesDownloadCount_WithoutReloadingTheList()
    {
        var fileId = Guid.NewGuid();
        var requestCount = 0;
        var (sut, notifications, _, _) = CreateSut(_ =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new List<FileSummaryResponse> { ActiveFile(fileId) }) };
        });
        await sut.LoadCommand.ExecuteAsync(null);
        var requestsAfterLoad = requestCount;

        notifications.RaiseFileDownloaded(new FileDownloadedNotification(fileId, "document.pdf", DateTimeOffset.UtcNow));

        Assert.Equal(1, sut.Files[0].DownloadCount);
        Assert.Equal(requestsAfterLoad, requestCount); // no extra API call triggered by the event
    }

    [Fact]
    public async Task FileDownloadedEvent_ShowsATransientToastMessage()
    {
        var fileId = Guid.NewGuid();
        var (sut, notifications, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<FileSummaryResponse> { ActiveFile(fileId) })
        });
        await sut.LoadCommand.ExecuteAsync(null);

        notifications.RaiseFileDownloaded(new FileDownloadedNotification(fileId, "document.pdf", DateTimeOffset.UtcNow));

        Assert.True(sut.IsToastVisible);
        Assert.Contains("document.pdf", sut.ToastMessage);
    }

    [Fact]
    public async Task LogoutCommand_StopsNotifications_ClearsSession_ClearsFiles_AndNavigatesToLogin()
    {
        var (sut, notifications, navigation, session) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<FileSummaryResponse> { ActiveFile() })
        });
        await session.SetSessionAsync(new AuthResponse("jwt", DateTimeOffset.UtcNow.AddHours(1)), new UserResponse(Guid.NewGuid(), "a@b.com"));
        await sut.LoadCommand.ExecuteAsync(null);

        await sut.LogoutCommand.ExecuteAsync(null);

        Assert.Equal(1, notifications.StopCount);
        Assert.False(session.IsAuthenticated);
        Assert.Empty(sut.Files);
        Assert.Equal("//login", navigation.RootRoute);
    }

    [Fact]
    public async Task ReloadingReconcilesInPlace_ExistingFileItemViewModelInstanceIsUpdated_NotReplaced()
    {
        var fileId = Guid.NewGuid();
        var callCount = 0;
        var (sut, _, _, _) = CreateSut(_ =>
        {
            callCount++;
            var downloadCount = callCount == 1 ? 0 : 5;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new List<FileSummaryResponse>
                {
                    ActiveFile(fileId) with { DownloadCount = downloadCount }
                })
            };
        });

        await sut.LoadCommand.ExecuteAsync(null);
        var firstInstance = sut.Files[0];

        await sut.RefreshCommand.ExecuteAsync(null);

        Assert.Same(firstInstance, sut.Files[0]);
        Assert.Equal(5, sut.Files[0].DownloadCount);
    }
}
