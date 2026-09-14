using FileSharing.Mobile.Core.Models;

namespace FileSharing.Mobile.Core.Services.Upload;

public interface IFilePickerService
{
    /// <summary>Returns null when the user cancels the picker.</summary>
    Task<UploadableItem?> PickFileAsync(CancellationToken cancellationToken = default);
}
