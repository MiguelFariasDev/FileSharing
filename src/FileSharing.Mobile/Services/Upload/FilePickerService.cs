using FileSharing.Mobile.Models;
using Microsoft.Maui.Storage;

namespace FileSharing.Mobile.Services.Upload;

/// <summary>
/// Wraps the .NET MAUI FilePicker for single-file selection. FilePicker can only ever
/// return individual files — it has no concept of a directory, which is exactly why
/// folder selection needs its own platform-specific implementation
/// (see Platforms/Android/FolderPickerService.cs).
/// </summary>
public class FilePickerService : IFilePickerService
{
    private static readonly FilePickerFileType AllowedFileTypes = new(
        new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.Android] =
            [
                "application/pdf",
                "application/epub+zip",
                "image/jpeg",
                "image/png",
                "image/webp",
                "image/gif",
                "video/mp4",
                "video/webm",
                "video/quicktime",
                "video/x-matroska",
                "audio/mpeg",
                "audio/wav",
                "audio/ogg",
                "audio/mp4",
                "audio/aac",
                "audio/flac"
            ]
        });

    public async Task<UploadableItem?> PickFileAsync(CancellationToken cancellationToken = default)
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Selecione um arquivo",
            FileTypes = AllowedFileTypes
        });

        if (result is null)
            return null;

        var sizeBytes = new FileInfo(result.FullPath).Length;

        return new UploadableItem
        {
            LocalFilePath = result.FullPath,
            FileName = result.FileName,
            ContentType = string.IsNullOrWhiteSpace(result.ContentType) ? "application/octet-stream" : result.ContentType,
            SizeBytes = sizeBytes,
            IsFolder = false,
            IsTemporaryFile = false
        };
    }
}
