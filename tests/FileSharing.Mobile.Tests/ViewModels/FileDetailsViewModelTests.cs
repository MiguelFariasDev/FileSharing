using System.Net;
using System.Net.Http.Json;
using FileSharing.Application.DTOs.Files;
using FileSharing.Mobile.Core.Models;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Core.ViewModels;
using FileSharing.Mobile.Tests.Fakes;
using FileSharing.Mobile.Tests.Services;

namespace FileSharing.Mobile.Tests.ViewModels;

public class FileDetailsViewModelTests
{
    private static FileItemViewModel FileWithoutLink(Guid fileId) => new(new FileSummaryResponse(
        fileId, "document.pdf", "application/pdf", 2048, false, "Active",
        DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(23), 0, false));

    private static FileItemViewModel FileWithLink(Guid fileId) => new(new FileSummaryResponse(
        fileId, "document.pdf", "application/pdf", 2048, false, "Active",
        DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(23), 3, true));

    private static (FileDetailsViewModel ViewModel, FakeClipboardService Clipboard, FakeShareService Share, FakeNavigationService Navigation) CreateSut(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") };
        var apiClient = new FileSharingApiClient(httpClient, new AuthSession(new InMemorySecureStorageService()));
        var clipboard = new FakeClipboardService();
        var share = new FakeShareService();
        var navigation = new FakeNavigationService();

        return (new FileDetailsViewModel(apiClient, clipboard, share, navigation), clipboard, share, navigation);
    }

    [Fact]
    public async Task GenerateOrRegenerateClicked_WhenNoLinkExistsYet_GeneratesImmediately_WithoutConfirmation()
    {
        var fileId = Guid.NewGuid();
        var (sut, _, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new PublicLinkResponse(fileId, "first-token", "https://filesharing.test/api/public/files/first-token"))
        });
        sut.Initialize(FileWithoutLink(fileId));

        await sut.GenerateOrRegenerateClickedCommand.ExecuteAsync(null);

        Assert.False(sut.ShowRegenerateConfirm);
        Assert.Equal("https://filesharing.test/api/public/files/first-token", sut.GeneratedLink);
    }

    [Fact]
    public async Task GenerateOrRegenerateClicked_WhenALinkAlreadyExists_AsksForConfirmationFirst_WithoutCallingTheApi()
    {
        var fileId = Guid.NewGuid();
        var called = false;
        var (sut, _, _, _) = CreateSut(_ => { called = true; return new HttpResponseMessage(HttpStatusCode.OK); });
        sut.Initialize(FileWithLink(fileId));

        await sut.GenerateOrRegenerateClickedCommand.ExecuteAsync(null);

        Assert.True(sut.ShowRegenerateConfirm);
        Assert.False(called);
    }

    [Fact]
    public async Task ConfirmRegenerateAsync_CallsTheApi_AndShowsTheNewLink()
    {
        var fileId = Guid.NewGuid();
        var (sut, _, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new PublicLinkResponse(fileId, "new-token", "https://filesharing.test/api/public/files/new-token"))
        });
        sut.Initialize(FileWithLink(fileId));
        sut.GenerateOrRegenerateClickedCommand.Execute(null);

        await sut.ConfirmRegenerateCommand.ExecuteAsync(null);

        Assert.False(sut.ShowRegenerateConfirm);
        Assert.Equal("https://filesharing.test/api/public/files/new-token", sut.GeneratedLink);
    }

    [Fact]
    public void CancelRegenerate_DismissesTheConfirmation_WithoutCallingTheApi()
    {
        var fileId = Guid.NewGuid();
        var called = false;
        var (sut, _, _, _) = CreateSut(_ => { called = true; return new HttpResponseMessage(HttpStatusCode.OK); });
        sut.Initialize(FileWithLink(fileId));
        sut.GenerateOrRegenerateClickedCommand.Execute(null);

        sut.CancelRegenerateCommand.Execute(null);

        Assert.False(sut.ShowRegenerateConfirm);
        Assert.False(called);
        Assert.Null(sut.GeneratedLink);
    }

    [Fact]
    public async Task CopyLinkAsync_CopiesExactlyTheGeneratedLink()
    {
        var fileId = Guid.NewGuid();
        var (sut, clipboard, _, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new PublicLinkResponse(fileId, "token", "https://filesharing.test/api/public/files/token"))
        });
        sut.Initialize(FileWithoutLink(fileId));
        await sut.GenerateOrRegenerateClickedCommand.ExecuteAsync(null);

        await sut.CopyLinkCommand.ExecuteAsync(null);

        Assert.Equal("https://filesharing.test/api/public/files/token", clipboard.CopiedText);
    }

    [Fact]
    public void ViewHistory_NavigatesWithTheFilesId()
    {
        var fileId = Guid.NewGuid();
        var (sut, _, _, navigation) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK));
        sut.Initialize(FileWithLink(fileId));

        sut.ViewHistoryCommand.Execute(null);

        Assert.Contains(navigation.Visited, r => r.Contains(fileId.ToString()));
    }
}
