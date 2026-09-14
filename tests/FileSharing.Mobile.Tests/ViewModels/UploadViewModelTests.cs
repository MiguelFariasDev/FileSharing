using System.Net;
using System.Net.Http.Json;
using FileSharing.Application.DTOs.Files;
using FileSharing.Mobile.Core.Models;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Core.Services.Upload;
using FileSharing.Mobile.Core.ViewModels;
using FileSharing.Mobile.Tests.Fakes;
using FileSharing.Mobile.Tests.Services;

namespace FileSharing.Mobile.Tests.ViewModels;

public class UploadViewModelTests
{
    private static UploadableItem SampleItem => new()
    {
        LocalFilePath = "/tmp/document.pdf",
        FileName = "document.pdf",
        ContentType = "application/pdf",
        SizeBytes = 4096,
        IsFolder = false,
        IsTemporaryFile = false
    };

    private static (UploadViewModel ViewModel, FakeFileUploadService UploadService, FakeFilePickerService FilePicker,
        FakeClipboardService Clipboard, FakeShareService Share, FakeNavigationService Navigation)
        CreateSut(Func<HttpRequestMessage, HttpResponseMessage>? linkResponder = null)
    {
        var handler = new FakeHttpMessageHandler(linkResponder ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new PublicLinkResponse(Guid.NewGuid(), "token123", "https://filesharing.test/api/public/files/token123"))
        }));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") };
        var apiClient = new FileSharingApiClient(httpClient, new AuthSession(new InMemorySecureStorageService()));

        var filePicker = new FakeFilePickerService { ItemToReturn = SampleItem };
        var folderPicker = new FakeFolderPickerService();
        var uploadService = new FakeFileUploadService();
        var clipboard = new FakeClipboardService();
        var share = new FakeShareService();
        var navigation = new FakeNavigationService();
        var dispatcher = new ImmediateMainThreadDispatcher();

        var viewModel = new UploadViewModel(apiClient, filePicker, folderPicker, uploadService, clipboard, share, navigation, dispatcher);
        return (viewModel, uploadService, filePicker, clipboard, share, navigation);
    }

    [Fact]
    public async Task PickFileAsync_SetsSelectedItem()
    {
        var (sut, _, _, _, _, _) = CreateSut();

        await sut.PickFileCommand.ExecuteAsync(null);

        Assert.NotNull(sut.SelectedItem);
        Assert.True(sut.HasSelection);
    }

    [Fact]
    public async Task PickFileAsync_UserCancels_LeavesSelectionEmpty()
    {
        var (sut, _, filePicker, _, _, _) = CreateSut();
        filePicker.ItemToReturn = null;

        await sut.PickFileCommand.ExecuteAsync(null);

        Assert.False(sut.HasSelection);
    }

    [Fact]
    public async Task ConfirmUploadAsync_Success_GeneratesTheLink_AndReachesCompletedStage()
    {
        var (sut, uploadService, _, _, _, _) = CreateSut();
        uploadService.OutcomeToReturn = UploadOutcome.Success(new CompleteUploadResponse(Guid.NewGuid(), "document.pdf", 4096, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24)));
        await sut.PickFileCommand.ExecuteAsync(null);

        await sut.ConfirmUploadCommand.ExecuteAsync(null);

        Assert.Equal(UploadStage.Completed, sut.Stage);
        Assert.True(sut.IsCompleted);
        Assert.Equal("https://filesharing.test/api/public/files/token123", sut.GeneratedLink);
    }

    [Fact]
    public async Task ConfirmUploadAsync_Failure_ReachesFailedStage_WithTheOutcomesMessage_AndNeverGeneratesALink()
    {
        var linkCalled = false;
        var (sut, uploadService, _, _, _, _) = CreateSut(_ => { linkCalled = true; return new HttpResponseMessage(HttpStatusCode.OK); });
        uploadService.OutcomeToReturn = UploadOutcome.Failure(UploadFailureReason.StorageUploadFailed, "Não foi possível concluir o envio.");
        await sut.PickFileCommand.ExecuteAsync(null);

        await sut.ConfirmUploadCommand.ExecuteAsync(null);

        Assert.Equal(UploadStage.Failed, sut.Stage);
        Assert.Equal("Não foi possível concluir o envio.", sut.ErrorMessage);
        Assert.False(linkCalled);
        Assert.Null(sut.GeneratedLink);
    }

    [Fact]
    public async Task ConfirmUploadAsync_ReportsProgressUpdatesOntoTheViewModel()
    {
        var (sut, uploadService, _, _, _, _) = CreateSut();
        uploadService.ReportedProgress.Add(new UploadProgressUpdate(UploadStage.Uploading, 0.5, 2048, 4096));
        uploadService.OutcomeToReturn = UploadOutcome.Success(new CompleteUploadResponse(Guid.NewGuid(), "document.pdf", 4096, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24)));
        await sut.PickFileCommand.ExecuteAsync(null);

        var progressSnapshots = new List<double>();
        sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UploadViewModel.ProgressFraction))
                progressSnapshots.Add(sut.ProgressFraction);
        };

        await sut.ConfirmUploadCommand.ExecuteAsync(null);

        // Same ThreadPool-hop caveat as FileUploadServiceTests — the fake's progress.Report call
        // can still be in flight when ExecuteAsync itself has already returned.
        await TestWait.UntilAsync(() => progressSnapshots.Contains(0.5));
    }

    [Fact]
    public async Task CopyLinkAsync_CopiesTheGeneratedLink()
    {
        var (sut, uploadService, _, clipboard, _, _) = CreateSut();
        uploadService.OutcomeToReturn = UploadOutcome.Success(new CompleteUploadResponse(Guid.NewGuid(), "document.pdf", 4096, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24)));
        await sut.PickFileCommand.ExecuteAsync(null);
        await sut.ConfirmUploadCommand.ExecuteAsync(null);

        await sut.CopyLinkCommand.ExecuteAsync(null);

        Assert.Equal(sut.GeneratedLink, clipboard.CopiedText);
    }

    [Fact]
    public async Task ShareLinkAsync_UsesTheNativeShareMechanism_WithTheGeneratedLink()
    {
        var (sut, uploadService, _, _, share, _) = CreateSut();
        uploadService.OutcomeToReturn = UploadOutcome.Success(new CompleteUploadResponse(Guid.NewGuid(), "document.pdf", 4096, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24)));
        await sut.PickFileCommand.ExecuteAsync(null);
        await sut.ConfirmUploadCommand.ExecuteAsync(null);

        await sut.ShareLinkCommand.ExecuteAsync(null);

        Assert.Equal(sut.GeneratedLink, share.SharedText);
    }

    [Fact]
    public void Reset_ClearsEveryPieceOfPreviousUploadState()
    {
        var (sut, _, _, _, _, _) = CreateSut();
        sut.SelectedItem = SampleItem;
        sut.ErrorMessage = "algo deu errado";

        sut.Reset();

        Assert.Null(sut.SelectedItem);
        Assert.Equal(UploadStage.Idle, sut.Stage);
        Assert.Null(sut.ErrorMessage);
        Assert.Null(sut.GeneratedLink);
        Assert.Equal(0, sut.ProgressFraction);
    }

    [Fact]
    public async Task CancelUpload_SignalsCancellationToTheUploadService_WhileAnUploadIsActuallyInFlight()
    {
        var (sut, uploadService, _, _, _, _) = CreateSut();
        // Blocks UploadAsync until either the test or the cancellation itself unblocks it — a
        // fake that returned immediately would leave no real in-flight window to cancel.
        uploadService.PauseBeforeReturning = new TaskCompletionSource<bool>();
        await sut.PickFileCommand.ExecuteAsync(null);

        var uploadTask = sut.ConfirmUploadCommand.ExecuteAsync(null);
        sut.CancelUploadCommand.Execute(null);
        await uploadTask;

        Assert.True(uploadService.LastCancellationToken.IsCancellationRequested);
        Assert.Equal(UploadStage.Failed, sut.Stage);
        Assert.Equal("Envio cancelado.", sut.ErrorMessage);
    }
}
