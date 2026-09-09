using FileSharing.Domain.Enums;
using File = FileSharing.Domain.Entities.File;

namespace FileSharing.UnitTests.Domain.Entities;

public class FileTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static File CreateFile(bool isFolder = false, CompressionType compressionType = CompressionType.None, long sizeBytes = 1024) =>
        new(UserId, "document.pdf", "storage-key", "application/pdf", sizeBytes, isFolder, compressionType);

    [Fact]
    public void Constructor_StartsAsPendingUpload()
    {
        var file = CreateFile();

        Assert.Equal(FileStatus.PendingUpload, file.Status);
        Assert.True(file.IsPendingUpload);
    }

    [Fact]
    public void Constructor_IsNotActiveBeforeCompletion()
    {
        var file = CreateFile();

        Assert.False(file.IsActive);
        Assert.Null(file.CreatedAt);
        Assert.Null(file.ExpiresAt);
    }

    [Fact]
    public void CompleteUpload_ChangesStatusToActive()
    {
        var file = CreateFile();

        file.CompleteUpload(DateTimeOffset.UtcNow);

        Assert.Equal(FileStatus.Active, file.Status);
        Assert.True(file.IsActive);
    }

    [Fact]
    public void CompleteUpload_SetsCreatedAtToCompletionMoment()
    {
        var file = CreateFile();
        var completedAt = DateTimeOffset.UtcNow.AddMinutes(5);

        file.CompleteUpload(completedAt);

        Assert.Equal(completedAt, file.CreatedAt);
    }

    [Fact]
    public void CompleteUpload_SetsExpiresAtExactly24HoursAfterCreatedAt()
    {
        var file = CreateFile();
        var completedAt = DateTimeOffset.UtcNow.AddMinutes(5);

        file.CompleteUpload(completedAt);

        Assert.Equal(completedAt.AddHours(24), file.ExpiresAt);
    }

    [Fact]
    public void CompleteUpload_Throws_WhenNotPendingUpload()
    {
        var file = CreateFile();
        file.CompleteUpload(DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => file.CompleteUpload(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Constructor_AllowsZipCompression_WhenIsFolder()
    {
        var file = CreateFile(isFolder: true, compressionType: CompressionType.Zip);

        Assert.True(file.IsFolder);
        Assert.Equal(CompressionType.Zip, file.CompressionType);
    }

    [Fact]
    public void Constructor_UsesNoneCompression_ForRegularFile()
    {
        var file = CreateFile(isFolder: false, compressionType: CompressionType.None);

        Assert.False(file.IsFolder);
        Assert.Equal(CompressionType.None, file.CompressionType);
    }

    [Fact]
    public void Constructor_Throws_WhenFolderDoesNotUseZip()
    {
        Assert.Throws<ArgumentException>(() => CreateFile(isFolder: true, compressionType: CompressionType.None));
    }

    [Fact]
    public void Constructor_Throws_WhenRegularFileUsesZip()
    {
        Assert.Throws<ArgumentException>(() => CreateFile(isFolder: false, compressionType: CompressionType.Zip));
    }

    [Fact]
    public void Constructor_Throws_WhenSizeIsInvalid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateFile(sizeBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateFile(sizeBytes: -1));
    }

    [Fact]
    public void Constructor_Throws_WhenNameIsInvalid()
    {
        Assert.Throws<ArgumentException>(() =>
            new File(UserId, "   ", "storage-key", "application/pdf", 1024, false, CompressionType.None));
    }

    [Fact]
    public void IsExpired_ReturnsFalse_ForPendingUpload()
    {
        var file = CreateFile();

        Assert.False(file.IsExpired(DateTimeOffset.UtcNow.AddYears(1)));
    }

    [Fact]
    public void IsExpired_ReturnsTrue_WhenCurrentTimeIsAtOrAfterExpiresAt()
    {
        var file = CreateFile();
        var completedAt = DateTimeOffset.UtcNow;
        file.CompleteUpload(completedAt);

        Assert.True(file.IsExpired(file.ExpiresAt!.Value));
        Assert.False(file.IsExpired(file.ExpiresAt.Value.AddMinutes(-1)));
    }

    [Fact]
    public void MarkAsExpired_SetsStatusToExpired()
    {
        var file = CreateFile();
        file.CompleteUpload(DateTimeOffset.UtcNow);

        file.MarkAsExpired();

        Assert.Equal(FileStatus.Expired, file.Status);
    }
}
