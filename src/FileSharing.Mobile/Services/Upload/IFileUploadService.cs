using FileSharing.Application.DTOs.Files;
using FileSharing.Mobile.Models;

namespace FileSharing.Mobile.Services.Upload;

public interface IFileUploadService
{
    /// <summary>
    /// Initiates the upload, PUTs the file's bytes straight to the presigned URL (never
    /// through the API), then confirms completion. If <paramref name="cancellationToken"/>
    /// is cancelled mid-transfer, the PUT is aborted and complete is never called — the
    /// file stays PendingUpload server-side, exactly as it should.
    /// </summary>
    Task<CompleteUploadResponse> UploadAsync(
        UploadableItem item,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default);
}
