using FileSharing.Mobile.Models;

namespace FileSharing.Mobile.Services.Upload;

public interface IFolderPickerService
{
    /// <summary>
    /// Lets the user pick a folder (Android Storage Access Framework), then zips its
    /// contents — preserving the relative folder structure and the original folder name
    /// as the zip's root — into a temporary file. Returns null when the user cancels.
    /// The zip uses lossless (Deflate) compression; no individual file inside it is
    /// re-encoded or re-compressed.
    /// </summary>
    Task<UploadableItem?> PickFolderAndZipAsync(IProgress<double>? progress, CancellationToken cancellationToken = default);
}
