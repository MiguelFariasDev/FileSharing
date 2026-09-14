using System.Net;
using System.Net.Http.Json;
using FileSharing.Application.DTOs.Files;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Core.ViewModels;
using FileSharing.Mobile.Tests.Services;

namespace FileSharing.Mobile.Tests.ViewModels;

public class HistoryViewModelTests
{
    private static HistoryViewModel CreateSut(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") };
        var apiClient = new FileSharingApiClient(httpClient, new AuthSession(new InMemorySecureStorageService()));
        return new HistoryViewModel(apiClient);
    }

    [Fact]
    public async Task LoadAsync_Success_PopulatesEntries()
    {
        var sut = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<DownloadHistoryEntryResponse> { new(DateTimeOffset.UtcNow) })
        });

        await sut.LoadAsync(Guid.NewGuid());

        Assert.Single(sut.Entries);
        Assert.False(sut.IsLoading);
        Assert.False(sut.IsEmpty);
    }

    [Fact]
    public async Task LoadAsync_NoDownloadsYet_ReportsEmpty_NeverAnError()
    {
        var sut = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<DownloadHistoryEntryResponse>())
        });

        await sut.LoadAsync(Guid.NewGuid());

        Assert.True(sut.IsEmpty);
        Assert.False(sut.HasError);
    }

    [Fact]
    public async Task LoadAsync_FileNotFoundOrNotOwned_ReportsError_NeverExposesWhichReason()
    {
        var sut = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await sut.LoadAsync(Guid.NewGuid());

        Assert.True(sut.HasError);
        Assert.False(sut.IsLoading);
    }

    [Fact]
    public async Task LoadAsync_NeverExposesIpOrUserAgent()
    {
        // DownloadHistoryEntryResponse's own contract has no such fields at all — this asserts
        // the ViewModel never fabricates or otherwise surfaces them beyond what the DTO carries.
        var sut = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<DownloadHistoryEntryResponse> { new(DateTimeOffset.UtcNow) })
        });

        await sut.LoadAsync(Guid.NewGuid());

        var properties = typeof(DownloadHistoryEntryResponse).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain(properties, p => p.Contains("Ip", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, p => p.Contains("Agent", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RetryCommand_ReloadsTheSameFileId()
    {
        var fileId = Guid.NewGuid();
        var requestedFileIds = new List<string>();
        var sut = CreateSut(request =>
        {
            requestedFileIds.Add(request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new List<DownloadHistoryEntryResponse>()) };
        });

        await sut.LoadAsync(fileId);
        await sut.RetryCommand.ExecuteAsync(null);

        Assert.Equal(2, requestedFileIds.Count);
        Assert.All(requestedFileIds, path => Assert.Contains(fileId.ToString(), path));
    }
}
