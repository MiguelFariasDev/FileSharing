using System.Net.Http.Headers;
using FileSharing.Application.DTOs.Files;
using FileSharing.Mobile.Core.Models;
using FileSharing.Mobile.Core.Services.ApiClient;

namespace FileSharing.Mobile.Core.Services.Upload;

/// <summary>
/// Orchestrates the three-step flow (initiate -> PUT straight to S3 -> complete) — the API
/// itself never sees the file's bytes; only the presigned URL crosses from API to this client,
/// and the raw content only ever flows Mobile -> S3 directly (Fase 13 §3).
///
/// No automatic retry anywhere in here, deliberately (Fase 13 §17): initiate is not safe to
/// retry blindly (each call creates a brand new PendingUpload row server-side), and a failed PUT
/// cannot be resumed against the same presigned URL (a single-PUT presigned upload has no
/// partial-resume semantics) — the only supported recovery from any failure is the caller
/// re-invoking UploadAsync from scratch with the same UploadableItem, which correctly starts an
/// entirely new upload (new FileId, new presigned URL) rather than reusing anything stale.
/// </summary>
public class FileUploadService : IFileUploadService
{
    private readonly FileSharingApiClient _apiClient;
    private readonly S3UploadHttpClient _uploadHttpClient;

    public FileUploadService(FileSharingApiClient apiClient, S3UploadHttpClient uploadHttpClient)
    {
        _apiClient = apiClient;

        // Deliberately a distinctly-typed, bare HttpClient (constructor-injected so tests can
        // assert on exactly this call without a real network — see S3UploadHttpClient's own
        // remarks): the presigned URL already carries its own authorization, and it must never
        // receive this app's API bearer token or base address — this request goes straight to
        // S3, never to the FileSharing API.
        _uploadHttpClient = uploadHttpClient;
    }

    public async Task<UploadOutcome> UploadAsync(
        UploadableItem item,
        IProgress<UploadProgressUpdate>? progress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            progress?.Report(new UploadProgressUpdate(UploadStage.Preparing, 0, 0, item.SizeBytes));

            var initiateResult = await _apiClient.InitiateUploadAsync(
                new InitiateUploadRequest(item.FileName, item.ContentType, item.SizeBytes, item.IsFolder),
                cancellationToken);

            if (!initiateResult.IsSuccess)
            {
                var reason = initiateResult.ErrorType == ApiErrorType.Network ? UploadFailureReason.Network : UploadFailureReason.ValidationFailed;
                return UploadOutcome.Failure(reason, initiateResult.Message ?? "Não foi possível iniciar o envio.");
            }

            var initiated = initiateResult.Value!;

            try
            {
                await PutFileContentAsync(initiated.UploadUrl, item, progress, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return UploadOutcome.Failure(UploadFailureReason.Cancelled, "Envio cancelado.");
            }
            catch (Exception)
            {
                // Covers an expired/invalid presigned URL, a real S3-side error, and a network
                // failure mid-transfer alike — the file stays PendingUpload server-side either
                // way (complete is never called), so nothing here needs to be undone.
                return UploadOutcome.Failure(
                    UploadFailureReason.StorageUploadFailed,
                    "Não foi possível concluir o envio. Verifique sua conexão e tente novamente.");
            }

            progress?.Report(new UploadProgressUpdate(UploadStage.Completing, 1, item.SizeBytes, item.SizeBytes));

            var completeResult = await _apiClient.CompleteUploadAsync(initiated.FileId, cancellationToken);

            if (!completeResult.IsSuccess)
            {
                var reason = completeResult.ErrorType == ApiErrorType.Network ? UploadFailureReason.Network : UploadFailureReason.CompleteFailed;
                return UploadOutcome.Failure(reason, completeResult.Message ?? "Não foi possível concluir o envio.");
            }

            progress?.Report(new UploadProgressUpdate(UploadStage.Completed, 1, item.SizeBytes, item.SizeBytes));
            return UploadOutcome.Success(completeResult.Value!);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return UploadOutcome.Failure(UploadFailureReason.Cancelled, "Envio cancelado.");
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
        IProgress<UploadProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        await using var fileStream = File.OpenRead(item.LocalFilePath);

        var byteProgress = progress is null
            ? null
            : new Progress<double>(fraction => progress.Report(new UploadProgressUpdate(
                UploadStage.Uploading, fraction, (long)(fraction * item.SizeBytes), item.SizeBytes)));

        using var progressStream = new ProgressReportingStream(fileStream, byteProgress);
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
