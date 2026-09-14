using FileSharing.Mobile.Core.Models;

namespace FileSharing.Mobile.Core.Services.Upload;

public interface IFileUploadService
{
    Task<UploadOutcome> UploadAsync(
        UploadableItem item,
        IProgress<UploadProgressUpdate>? progress,
        CancellationToken cancellationToken = default);
}
