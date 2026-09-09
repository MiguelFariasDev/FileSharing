using FileSharing.Domain.Enums;

namespace FileSharing.Domain.Entities;

public class File
{
    public const int ExpirationHours = 24;

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    public string StorageKey { get; private set; } = string.Empty;
    public string AccessTokenHash { get; private set; } = string.Empty;
    public FileStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public User User { get; private set; } = null!;
    public ICollection<Download> Downloads { get; private set; } = new List<Download>();

    private File()
    {
    }

    public File(Guid userId, string originalFileName, string storageKey, string accessTokenHash)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId is required.", nameof(userId));

        if (string.IsNullOrWhiteSpace(originalFileName))
            throw new ArgumentException("OriginalFileName is required.", nameof(originalFileName));

        if (string.IsNullOrWhiteSpace(storageKey))
            throw new ArgumentException("StorageKey is required.", nameof(storageKey));

        if (string.IsNullOrWhiteSpace(accessTokenHash))
            throw new ArgumentException("AccessTokenHash is required.", nameof(accessTokenHash));

        Id = Guid.NewGuid();
        UserId = userId;
        OriginalFileName = originalFileName;
        StorageKey = storageKey;
        AccessTokenHash = accessTokenHash;
        Status = FileStatus.Active;
        CreatedAt = DateTimeOffset.UtcNow;
        ExpiresAt = CreatedAt.AddHours(ExpirationHours);
    }

    public bool IsExpired() => IsExpired(DateTimeOffset.UtcNow);

    public bool IsExpired(DateTimeOffset asOfUtc) => asOfUtc >= ExpiresAt;

    public void MarkAsExpired() => Status = FileStatus.Expired;
}
