using FileSharing.Domain.Enums;
using File = FileSharing.Domain.Entities.File;

namespace FileSharing.UnitTests.Domain.Entities;

public class FileTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static File CreateFile() =>
        new(UserId, "document.pdf", "storage-key", "access-token-hash");

    [Fact]
    public void Constructor_SetsStatusToActive()
    {
        var file = CreateFile();

        Assert.Equal(FileStatus.Active, file.Status);
    }

    [Fact]
    public void Constructor_SetsExpiresAtExactly24HoursAfterCreatedAt()
    {
        var file = CreateFile();

        Assert.Equal(file.CreatedAt.AddHours(24), file.ExpiresAt);
    }

    [Fact]
    public void IsExpired_ReturnsFalse_BeforeExpiration()
    {
        var file = CreateFile();

        var result = file.IsExpired(file.ExpiresAt.AddMinutes(-1));

        Assert.False(result);
    }

    [Fact]
    public void IsExpired_ReturnsTrue_WhenCurrentTimeIsAtOrAfterExpiresAt()
    {
        var file = CreateFile();

        Assert.True(file.IsExpired(file.ExpiresAt));
        Assert.True(file.IsExpired(file.ExpiresAt.AddMinutes(1)));
    }

    [Fact]
    public void MarkAsExpired_SetsStatusToExpired()
    {
        var file = CreateFile();

        file.MarkAsExpired();

        Assert.Equal(FileStatus.Expired, file.Status);
    }
}
