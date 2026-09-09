using System.Net.Http.Headers;
using FileSharing.Application.DTOs.Files;
using FileSharing.Mobile.Models;
using FileSharing.Mobile.Services.Api;

namespace FileSharing.Mobile.Services.Upload;

public class FileUploadService : IFileUploadService
{
    private readonly FileSharingApiClient _apiClient;
    private readonly HttpClient _uploadHttpClient;

    public FileUploadService(FileSharingApiClient apiClient)
    {
        _apiClient = apiClient;

        // Deliberately a separate, bare HttpClient: the presigned URL already carries its
        // own authorization, and it must never receive this app's API bearer token or base
        // address — this request goes straight to S3, not to the FileSharing API.
        _uploadHttpClient = new HttpClient();
    }

    public async Task<CompleteUploadResponse> UploadAsync(
        UploadableItem item,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var initiated = await _apiClient.InitiateUploadAsync(
                new InitiateUploadRequest(item.FileName, item.ContentType, item.SizeBytes, item.IsFolder),
                cancellationToken);

            await PutFileContentAsync(initiated.UploadUrl, item, progress, cancellationToken);

            // Only reached if the PUT above completed successfully — a cancellation or a
            // network failure during the transfer throws before this point, and the file
            // is left PendingUpload server-side rather than being (incorrectly) completed.
            return await _apiClient.CompleteUploadAsync(initiated.FileId, cancellationToken);
        }
        finally
        {
            if (item.IsTemporaryFile)
                TryDeleteFile(item.LocalFilePath);
        }
    }

    private async Task PutFileContentAsync(
        string uploadUrl,
        UploadableItem item,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        await using var fileStream = File.OpenRead(item.LocalFilePath);
        using var progressStream = new ProgressReportingStream(fileStream, progress);
        using var content = new StreamContent(progressStream);
        content.Headers.ContentType = new MediaTypeHeaderValue(item.ContentType);
        content.Headers.ContentLength = item.SizeBytes;

        using var response = await _uploadHttpClient.PutAsync(uploadUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup — an orphaned temp zip here is not worth failing the
            // upload result over.
        }
    }
}
