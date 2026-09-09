namespace FileSharing.Application.Abstractions.Storage;

public class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    public string BucketName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public int PresignedUploadExpirationMinutes { get; set; } = 15;

    /// <summary>
    /// Ceiling for how large an uploaded object may be. A presigned single-PUT upload
    /// does not scale indefinitely; if this needs to grow well beyond the low single-digit
    /// gigabytes, <see cref="IFileStorageService"/> is the seam where a
    /// multipart-upload implementation would be introduced without touching callers.
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 5L * 1024 * 1024 * 1024;
}
