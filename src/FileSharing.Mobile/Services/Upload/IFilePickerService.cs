using FileSharing.Mobile.Models;

namespace FileSharing.Mobile.Services.Upload;

public interface IFilePickerService
{
    /// <summary>Returns null when the user cancels the picker.</summary>
    Task<UploadableItem?> PickFileAsync(CancellationToken cancellationToken = default);
}
