namespace FileSharing.Application.Abstractions.Storage;

/// <summary>
/// Abstracts the object storage provider from the rest of the application. The current
/// implementation targets AWS S3 (or an S3-compatible endpoint such as LocalStack), but
/// no AWS-specific type is referenced outside FileSharing.Infrastructure.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Creates a short-lived presigned URL the client can use to PUT the object's content
    /// directly to storage. The file's content never flows through this API.
    /// </summary>
    Task<PresignedUploadUrl> CreatePresignedUploadUrlAsync(
        string storageKey,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<bool> ObjectExistsAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns metadata for the given object, or null when it does not exist.
    /// </summary>
    Task<StorageObjectMetadata?> GetObjectMetadataAsync(string storageKey, CancellationToken cancellationToken = default);

    Task DeleteObjectAsync(string storageKey, CancellationToken cancellationToken = default);
}

public record PresignedUploadUrl(string Url, DateTimeOffset ExpiresAt);

public record StorageObjectMetadata(long SizeBytes, string? ContentType);
