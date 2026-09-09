using FileSharing.Domain.Enums;

namespace FileSharing.Domain.Entities;

public class File
{
    public const int ExpirationHours = 24;

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    public string StorageKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public bool IsFolder { get; private set; }
    public CompressionType CompressionType { get; private set; }
    public string? AccessTokenHash { get; private set; }
    public FileStatus Status { get; private set; }
    public DateTimeOffset? CreatedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }

    public User User { get; private set; } = null!;
    public ICollection<Download> Downloads { get; private set; } = new List<Download>();

    private File()
    {
    }

    public File(
        Guid userId,
        string originalFileName,
        string storageKey,
        string contentType,
        long sizeBytes,
        bool isFolder,
        CompressionType compressionType)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId is required.", nameof(userId));

        if (string.IsNullOrWhiteSpace(originalFileName))
            throw new ArgumentException("OriginalFileName is required.", nameof(originalFileName));

        if (string.IsNullOrWhiteSpace(storageKey))
            throw new ArgumentException("StorageKey is required.", nameof(storageKey));

        if (string.IsNullOrWhiteSpace(contentType))
            throw new ArgumentException("ContentType is required.", nameof(contentType));

        if (sizeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "SizeBytes must be greater than zero.");

        if (isFolder && compressionType != CompressionType.Zip)
            throw new ArgumentException("A folder upload must use CompressionType.Zip.", nameof(compressionType));

        if (!isFolder && compressionType != CompressionType.None)
            throw new ArgumentException("A single-file upload must use CompressionType.None.", nameof(compressionType));

        Id = Guid.NewGuid();
        UserId = userId;
        OriginalFileName = originalFileName;
        StorageKey = storageKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        IsFolder = isFolder;
        CompressionType = compressionType;
        Status = FileStatus.PendingUpload;
    }

    public bool IsPendingUpload => Status == FileStatus.PendingUpload;

    public bool IsActive => Status == FileStatus.Active;

    /// <summary>
    /// Transitions the file from PendingUpload to Active once the upload has been
    /// confirmed against storage. CreatedAt/ExpiresAt are anchored to the moment the
    /// upload actually completed, never to when the presigned URL was issued.
    /// </summary>
    public void CompleteUpload(DateTimeOffset completedAtUtc)
    {
        if (Status != FileStatus.PendingUpload)
            throw new InvalidOperationException($"Cannot complete an upload for a file with status '{Status}'.");

        Status = FileStatus.Active;
        CreatedAt = completedAtUtc;
        ExpiresAt = completedAtUtc.AddHours(ExpirationHours);
    }

    public bool IsExpired() => IsExpired(DateTimeOffset.UtcNow);

    public bool IsExpired(DateTimeOffset asOfUtc) => ExpiresAt is not null && asOfUtc >= ExpiresAt.Value;

    public void MarkAsExpired()
    {
        if (Status != FileStatus.Active)
            throw new InvalidOperationException($"Cannot expire a file with status '{Status}'.");

        Status = FileStatus.Expired;
    }
}
