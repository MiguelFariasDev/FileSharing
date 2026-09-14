using System.Net;
using System.Net.Http.Json;
using FileSharing.Application.DTOs.Files;
using FileSharing.Mobile.Core.Models;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Core.Services.Upload;
using FileSharing.Mobile.Tests.Services;

namespace FileSharing.Mobile.Tests.Services.Upload;

public class FileUploadServiceTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"upload-test-{Guid.NewGuid():N}.pdf");

    public FileUploadServiceTests()
    {
        System.IO.File.WriteAllBytes(_tempFile, "%PDF-1.4 fake content"u8.ToArray());
    }

    public void Dispose()
    {
        if (System.IO.File.Exists(_tempFile))
            System.IO.File.Delete(_tempFile);
    }

    private UploadableItem CreateItem(bool isTemporary = false) => new()
    {
        LocalFilePath = _tempFile,
        FileName = "document.pdf",
        ContentType = "application/pdf",
        SizeBytes = new FileInfo(_tempFile).Length,
        IsFolder = false,
        IsTemporaryFile = isTemporary
    };

    private static FileSharingApiClient CreateApiClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") };
        return new FileSharingApiClient(httpClient, new AuthSession(new InMemorySecureStorageService()));
    }

    [Fact]
    public async Task UploadAsync_FullFlow_ReturnsSuccess_WithTheCompleteResponse()
    {
        var fileId = Guid.NewGuid();
        var apiClient = CreateApiClient(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/upload"))
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = JsonContent.Create(new InitiateUploadResponse(fileId, "http://s3.test/bucket/key", DateTimeOffset.UtcNow.AddMinutes(15)))
                };
            if (request.RequestUri!.AbsolutePath.EndsWith("/complete"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new CompleteUploadResponse(fileId, "document.pdf", 22, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24)))
                };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var s3Handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = new FileUploadService(apiClient, new S3UploadHttpClient(s3Handler));

        var outcome = await sut.UploadAsync(CreateItem(), progress: null);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(fileId, outcome.Result!.FileId);
    }

    [Fact]
    public async Task UploadAsync_PutsDirectlyToTheProvidedPresignedUrl_NeverToTheApi()
    {
        var apiClient = CreateApiClient(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/upload"))
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = JsonContent.Create(new InitiateUploadResponse(Guid.NewGuid(), "http://s3.test/bucket/presigned-key", DateTimeOffset.UtcNow.AddMinutes(15)))
                };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new CompleteUploadResponse(Guid.NewGuid(), "document.pdf", 22, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24)))
            };
        });

        var s3Handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = new FileUploadService(apiClient, new S3UploadHttpClient(s3Handler));

        await sut.UploadAsync(CreateItem(), progress: null);

        var putRequest = Assert.Single(s3Handler.Requests);
        Assert.Equal(HttpMethod.Put, putRequest.Method);
        Assert.Equal("http://s3.test/bucket/presigned-key", putRequest.RequestUri!.ToString());
        // The presigned URL already carries its own authorization — this client must never
        // attach the app's own bearer token to a request that goes straight to S3.
        Assert.Null(putRequest.Headers.Authorization);
    }

    [Fact]
    public async Task UploadAsync_ReportsRealByteDrivenProgress_NotJustATimeBasedGuess()
    {
        var apiClient = CreateApiClient(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/upload")
                ? new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(new InitiateUploadResponse(Guid.NewGuid(), "http://s3.test/key", DateTimeOffset.UtcNow.AddMinutes(15))) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new CompleteUploadResponse(Guid.NewGuid(), "document.pdf", 22, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24))) });

        var s3Handler = new FakeHttpMessageHandler(request =>
        {
            // Actually reads the stream, the same way a real HttpClient transport would —
            // ProgressReportingStream only reports as bytes are genuinely consumed.
            _ = request.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var sut = new FileUploadService(apiClient, new S3UploadHttpClient(s3Handler));

        var updates = new List<UploadProgressUpdate>();
        var progress = new Progress<UploadProgressUpdate>(updates.Add);

        await sut.UploadAsync(CreateItem(), progress);

        // Progress<T> has no SynchronizationContext to post through in a plain xunit test, so it
        // falls back to ThreadPool.QueueUserWorkItem — the callback can genuinely still be in
        // flight on another thread after UploadAsync itself has already returned.
        await TestWait.UntilAsync(() => updates.Any(u => u.Stage == FileSharing.Mobile.Core.Models.UploadStage.Uploading && u.Fraction >= 1.0));
    }

    [Fact]
    public async Task UploadAsync_WhenInitiateIsRejected_ReturnsValidationFailed_AndNeverCallsS3()
    {
        var apiClient = CreateApiClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var s3Handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = new FileUploadService(apiClient, new S3UploadHttpClient(s3Handler));

        var outcome = await sut.UploadAsync(CreateItem(), progress: null);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(UploadFailureReason.ValidationFailed, outcome.FailureReason);
        Assert.Empty(s3Handler.Requests);
    }

    [Fact]
    public async Task UploadAsync_WhenTheS3PutFails_ReturnsStorageUploadFailed_AndNeverCallsComplete()
    {
        var apiClient = CreateApiClient(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/upload")
                ? new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(new InitiateUploadResponse(Guid.NewGuid(), "http://s3.test/key", DateTimeOffset.UtcNow.AddMinutes(15))) }
                : throw new InvalidOperationException("complete should never be called when the PUT itself failed"));

        // Expired/invalid presigned URL, or any real S3-side error, looks like this: a non-2xx
        // response from the PUT.
        var s3Handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var sut = new FileUploadService(apiClient, new S3UploadHttpClient(s3Handler));

        var outcome = await sut.UploadAsync(CreateItem(), progress: null);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(UploadFailureReason.StorageUploadFailed, outcome.FailureReason);
    }

    [Fact]
    public async Task UploadAsync_WhenCompleteIsRejected_ReturnsCompleteFailed()
    {
        var apiClient = CreateApiClient(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/upload")
                ? new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(new InitiateUploadResponse(Guid.NewGuid(), "http://s3.test/key", DateTimeOffset.UtcNow.AddMinutes(15))) }
                : new HttpResponseMessage(HttpStatusCode.Conflict));

        var s3Handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = new FileUploadService(apiClient, new S3UploadHttpClient(s3Handler));

        var outcome = await sut.UploadAsync(CreateItem(), progress: null);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(UploadFailureReason.CompleteFailed, outcome.FailureReason);
    }

    [Fact]
    public async Task UploadAsync_Cancelled_ReturnsCancelled_NotAGenericFailure()
    {
        var apiClient = CreateApiClient(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new InitiateUploadResponse(Guid.NewGuid(), "http://s3.test/key", DateTimeOffset.UtcNow.AddMinutes(15)))
        });

        using var cts = new CancellationTokenSource();
        var s3Handler = new FakeHttpMessageHandler(_ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        var sut = new FileUploadService(apiClient, new S3UploadHttpClient(s3Handler));

        var outcome = await sut.UploadAsync(CreateItem(), progress: null, cts.Token);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(UploadFailureReason.Cancelled, outcome.FailureReason);
    }

    [Fact]
    public async Task UploadAsync_WithATemporaryFile_DeletesItAfterwards_SuccessOrFailure()
    {
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"folder-zip-{Guid.NewGuid():N}.zip");
        System.IO.File.WriteAllBytes(temporaryPath, "PK fake zip"u8.ToArray());
        var item = new UploadableItem
        {
            LocalFilePath = temporaryPath,
            FileName = "folder.zip",
            ContentType = "application/zip",
            SizeBytes = new FileInfo(temporaryPath).Length,
            IsFolder = true,
            IsTemporaryFile = true
        };

        var apiClient = CreateApiClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)); // fails fast, before any PUT
        var s3Handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = new FileUploadService(apiClient, new S3UploadHttpClient(s3Handler));

        await sut.UploadAsync(item, progress: null);

        Assert.False(System.IO.File.Exists(temporaryPath));
    }

    [Fact]
    public async Task UploadAsync_WithANonTemporaryFile_NeverDeletesTheOriginal()
    {
        var apiClient = CreateApiClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var s3Handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = new FileUploadService(apiClient, new S3UploadHttpClient(s3Handler));

        await sut.UploadAsync(CreateItem(isTemporary: false), progress: null);

        Assert.True(System.IO.File.Exists(_tempFile));
    }
}
