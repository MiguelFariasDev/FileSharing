namespace FileSharing.Mobile.Models;

/// <summary>
/// A single file (or a folder already zipped into one file) ready to be sent to the API.
/// For a folder, <see cref="LocalFilePath"/> points at a temporary zip created on-device;
/// <see cref="IsTemporaryFile"/> tells the caller it must delete that file once the upload
/// finishes or is cancelled.
/// </summary>
public class UploadableItem
{
    public required string LocalFilePath { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }
    public bool IsFolder { get; init; }
    public bool IsTemporaryFile { get; init; }
}
