using FileSharing.Application.Abstractions.Notifications;
using FileSharing.Application.Abstractions.Storage;
using FileSharing.Application.Common;
using FileSharing.Application.DTOs.Notifications;
using FileSharing.Application.Services.Files;
using FileSharing.Domain.Enums;
using FileSharing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FileSharing.UnitTests.Application.Files;

public class FileDownloadServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IFileStorageService> _storageMock = new();
    private readonly Mock<IFileDownloadNotifier> _notifierMock = new();
    private readonly FileDownloadService _sut;

    public FileDownloadServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _sut = new FileDownloadService(_dbContext, _storageMock.Object, _notifierMock.Object);
    }

    public void Dispose() => _dbContext.Dispose();

    private async Task<(Guid FileId, Guid OwnerUserId, string OriginalFileName, string AccessToken)> SeedActiveFileWithTokenAsync(DateTimeOffset? completedAt = null)
    {
        var ownerUserId = Guid.NewGuid();
        var file = new FileSharing.Domain.Entities.File(ownerUserId, "document.pdf", "storage-key", "application/pdf", 1024, false, CompressionType.None);
        var completed = completedAt ?? DateTimeOffset.UtcNow;
        file.CompleteUpload(completed);

        var accessToken = RandomTokenGenerator.Generate();
        file.AssignAccessToken(AccessTokenHasher.Hash(accessToken), completed);

        _dbContext.Files.Add(file);
        await _dbContext.SaveChangesAsync();

        return (file.Id, ownerUserId, file.OriginalFileName, accessToken);
    }

    private void SetupObjectExists(bool exists = true) =>
        _storageMock
            .Setup(s => s.ObjectExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exists);

    private void SetupPresignedDownloadUrl(string url = "https://mock-s3.test/download", DateTimeOffset? expiresAt = null) =>
        _storageMock
            .Setup(s => s.CreatePresignedDownloadUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PresignedDownloadUrl(url, expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(5)));

    [Fact]
    public async Task DownloadAsync_Succeeds_ForActiveNonExpiredFile_WithObjectPresent()
    {
        var (fileId, _, _, accessToken) = await SeedActiveFileWithTokenAsync();
        SetupObjectExists();
        SetupPresignedDownloadUrl("https://mock-s3.test/download-here");

        var result = await _sut.DownloadAsync(accessToken, "203.0.113.10", "TestAgent/1.0");

        Assert.True(result.IsSuccess);
        Assert.Equal("https://mock-s3.test/download-here", result.Value!.DownloadUrl);

        var download = await _dbContext.Downloads.SingleAsync(d => d.FileId == fileId);
        Assert.Equal(fileId, download.FileId);
    }

    [Fact]
    public async Task DownloadAsync_Fails_ForUnknownToken()
    {
        var result = await _sut.DownloadAsync("token-that-was-never-issued", "203.0.113.10", "TestAgent/1.0");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task DownloadAsync_Fails_ForExpiredFile()
    {
        var (_, _, _, accessToken) = await SeedActiveFileWithTokenAsync(completedAt: DateTimeOffset.UtcNow.AddHours(-25));
        SetupObjectExists();

        var result = await _sut.DownloadAsync(accessToken, "203.0.113.10", "TestAgent/1.0");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task DownloadAsync_Fails_ForFileThatIsNotActive()
    {
        // AssignAccessToken (Domain) refuses to hash a token onto a non-Active file, so a
        // token can never legitimately exist on one — this simulates that hypothetical/
        // corrupted state directly via the ChangeTracker to prove FileDownloadService itself
        // (not just token generation) refuses to serve a download for it.
        var (fileId, _, _, accessToken) = await SeedActiveFileWithTokenAsync();
        var file = await _dbContext.Files.SingleAsync(f => f.Id == fileId);
        _dbContext.Entry(file).Property("Status").CurrentValue = FileStatus.PendingUpload;
        await _dbContext.SaveChangesAsync();

        var result = await _sut.DownloadAsync(accessToken, "203.0.113.10", "TestAgent/1.0");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task DownloadAsync_Fails_WhenObjectMissingFromStorage_AndDoesNotRegisterADownload()
    {
        var (fileId, _, _, accessToken) = await SeedActiveFileWithTokenAsync();
        SetupObjectExists(exists: false);

        var result = await _sut.DownloadAsync(accessToken, "203.0.113.10", "TestAgent/1.0");

        Assert.False(result.IsSuccess);
        Assert.False(await _dbContext.Downloads.AnyAsync(d => d.FileId == fileId));
    }

    [Fact]
    public async Task DownloadAsync_UnknownAndExpiredTokens_ProduceTheSameOutcomeShape()
    {
        var (_, _, _, expiredToken) = await SeedActiveFileWithTokenAsync(completedAt: DateTimeOffset.UtcNow.AddHours(-25));
        SetupObjectExists();

        var expiredResult = await _sut.DownloadAsync(expiredToken, "203.0.113.10", "TestAgent/1.0");
        var unknownResult = await _sut.DownloadAsync("some-token-that-does-not-exist", "203.0.113.10", "TestAgent/1.0");

        Assert.Equal(expiredResult.IsSuccess, unknownResult.IsSuccess);
        Assert.Equal(expiredResult.Value, unknownResult.Value);
    }

    [Fact]
    public async Task DownloadAsync_RegistersADownload_WithTheGivenIpAndUserAgent()
    {
        var (fileId, _, _, accessToken) = await SeedActiveFileWithTokenAsync();
        SetupObjectExists();
        SetupPresignedDownloadUrl();

        await _sut.DownloadAsync(accessToken, "198.51.100.7", "Mozilla/5.0 TestClient");

        var download = await _dbContext.Downloads.SingleAsync(d => d.FileId == fileId);
        Assert.Equal("198.51.100.7", download.IpAddress);
        Assert.Equal("Mozilla/5.0 TestClient", download.UserAgent);
    }

    [Fact]
    public async Task DownloadAsync_DownloadedAtIsUtc_AndCapturedAtCallTime()
    {
        var (fileId, _, _, accessToken) = await SeedActiveFileWithTokenAsync();
        SetupObjectExists();
        SetupPresignedDownloadUrl();

        var before = DateTimeOffset.UtcNow;
        await _sut.DownloadAsync(accessToken, "203.0.113.10", "TestAgent/1.0");
        var after = DateTimeOffset.UtcNow;

        var download = await _dbContext.Downloads.SingleAsync(d => d.FileId == fileId);
        Assert.InRange(download.DownloadedAt, before, after);
        Assert.Equal(TimeSpan.Zero, download.DownloadedAt.Offset);
    }

    [Fact]
    public async Task DownloadAsync_CalledMultipleTimes_CreatesASeparateRecordEachTime()
    {
        var (fileId, _, _, accessToken) = await SeedActiveFileWithTokenAsync();
        SetupObjectExists();
        SetupPresignedDownloadUrl();

        await _sut.DownloadAsync(accessToken, "203.0.113.10", "TestAgent/1.0");
        await _sut.DownloadAsync(accessToken, "203.0.113.11", "TestAgent/2.0");
        await _sut.DownloadAsync(accessToken, "203.0.113.12", "TestAgent/3.0");

        var downloads = await _dbContext.Downloads.Where(d => d.FileId == fileId).ToListAsync();
        Assert.Equal(3, downloads.Count);
        Assert.Equal(3, downloads.Select(d => d.Id).Distinct().Count());
    }

    [Fact]
    public async Task DownloadAsync_DoesNotChangeFileStatus()
    {
        var (fileId, _, _, accessToken) = await SeedActiveFileWithTokenAsync();
        SetupObjectExists();
        SetupPresignedDownloadUrl();

        await _sut.DownloadAsync(accessToken, "203.0.113.10", "TestAgent/1.0");

        var file = await _dbContext.Files.SingleAsync(f => f.Id == fileId);
        Assert.Equal(FileStatus.Active, file.Status);
    }

    [Fact]
    public async Task DownloadAsync_RequestsAPresignedDownloadUrl_NeverAnUploadUrl()
    {
        var (_, _, _, accessToken) = await SeedActiveFileWithTokenAsync();
        SetupObjectExists();
        SetupPresignedDownloadUrl();

        await _sut.DownloadAsync(accessToken, "203.0.113.10", "TestAgent/1.0");

        _storageMock.Verify(s => s.CreatePresignedDownloadUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _storageMock.Verify(s => s.CreatePresignedUploadUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- Fase 7: notificação de download via IFileDownloadNotifier ---

    [Fact]
    public async Task DownloadAsync_ValidDownload_NotifiesTheOwner()
    {
        var (_, ownerUserId, _, accessToken) = await SeedActiveFileWithTokenAsync();
        SetupObjectExists();
        SetupPresignedDownloadUrl();

        await _sut.DownloadAsync(accessToken, "203.0.113.10", "TestAgent/1.0");

        _notifierMock.Verify(
            n => n.NotifyDownloadAsync(ownerUserId, It.IsAny<FileDownloadedNotification>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DownloadAsync_NotifiesExactlyTheFileOwner_NeverSomeoneElse()
    {
        var (_, ownerUserId, _, accessToken) = await SeedActiveFileWithTokenAsync();
        var (_, otherUserId, _, _) = await SeedActiveFileWithTokenAsync();
        SetupObjectExists();
        SetupPresignedDownloadUrl();

        await _sut.DownloadAsync(accessToken, "203.0.113.10", "TestAgent/1.0");

        _notifierMock.Verify(
            n => n.NotifyDownloadAsync(ownerUserId, It.IsAny<FileDownloadedNotification>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _notifierMock.Verify(
            n => n.NotifyDownloadAsync(otherUserId, It.IsAny<FileDownloadedNotification>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DownloadAsync_NotificationPayload_HasFileIdOriginalFileNameAndDownloadedAt()
    {
        var (fileId, ownerUserId, originalFileName, accessToken) = await SeedActiveFileWithTokenAsync();
        SetupObjectExists();
        SetupPresignedDownloadUrl();

        FileDownloadedNotification? captured = null;
        _notifierMock
            .Setup(n => n.NotifyDownloadAsync(ownerUserId, It.IsAny<FileDownloadedNotification>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, FileDownloadedNotification, CancellationToken>((_, notification, _) => captured = notification)
            .Returns(Task.CompletedTask);

        await _sut.DownloadAsync(accessToken, "203.0.113.10", "TestAgent/1.0");

        Assert.NotNull(captured);
        Assert.Equal(fileId, captured!.FileId);
        Assert.Equal(originalFileName, captured.OriginalFileName);

        var download = await _dbContext.Downloads.SingleAsync(d => d.FileId == fileId);
        Assert.Equal(download.DownloadedAt, captured.DownloadedAt);
    }

    [Fact]
    public void FileDownloadedNotification_ExposesOnlyFileIdOriginalFileNameAndDownloadedAt()
    {
        // Regression guard against ever widening the payload to include something sensitive
        // (AccessToken, presigned URL, downloader IP/UserAgent, StorageKey, ...) — see
        // IFileDownloadNotifier/FileDownloadedNotification remarks for what must never appear.
        var properties = typeof(FileDownloadedNotification).GetProperties().Select(p => p.Name).ToArray();

        Assert.Equal(
            new[] { nameof(FileDownloadedNotification.FileId), nameof(FileDownloadedNotification.OriginalFileName), nameof(FileDownloadedNotification.DownloadedAt) },
            properties);
    }

    [Fact]
    public async Task DownloadAsync_DownloadIsPersisted_BeforeTheNotifierIsCalled()
    {
        var (fileId, ownerUserId, _, accessToken) = await SeedActiveFileWithTokenAsync();
        SetupObjectExists();
        SetupPresignedDownloadUrl();

        var downloadWasPersistedWhenNotified = false;
        _notifierMock
            .Setup(n => n.NotifyDownloadAsync(ownerUserId, It.IsAny<FileDownloadedNotification>(), It.IsAny<CancellationToken>()))
            .Callback(() => downloadWasPersistedWhenNotified = _dbContext.Downloads.Any(d => d.FileId == fileId))
            .Returns(Task.CompletedTask);

        await _sut.DownloadAsync(accessToken, "203.0.113.10", "TestAgent/1.0");

        Assert.True(downloadWasPersistedWhenNotified);
    }

    [Fact]
    public async Task DownloadAsync_InvalidToken_NeverNotifiesAnyone()
    {
        await _sut.DownloadAsync("token-that-was-never-issued", "203.0.113.10", "TestAgent/1.0");

        _notifierMock.Verify(
            n => n.NotifyDownloadAsync(It.IsAny<Guid>(), It.IsAny<FileDownloadedNotification>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DownloadAsync_ExpiredFile_NeverNotifiesTheOwner()
    {
        var (_, ownerUserId, _, accessToken) = await SeedActiveFileWithTokenAsync(completedAt: DateTimeOffset.UtcNow.AddHours(-25));
        SetupObjectExists();

        await _sut.DownloadAsync(accessToken, "203.0.113.10", "TestAgent/1.0");

        _notifierMock.Verify(
            n => n.NotifyDownloadAsync(ownerUserId, It.IsAny<FileDownloadedNotification>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DownloadAsync_ObjectMissingFromStorage_NeverNotifiesTheOwner()
    {
        var (_, ownerUserId, _, accessToken) = await SeedActiveFileWithTokenAsync();
        SetupObjectExists(exists: false);

        await _sut.DownloadAsync(accessToken, "203.0.113.10", "TestAgent/1.0");

        _notifierMock.Verify(
            n => n.NotifyDownloadAsync(ownerUserId, It.IsAny<FileDownloadedNotification>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DownloadAsync_StillSucceeds_WhenTheNotifierThrows()
    {
        var (_, _, _, accessToken) = await SeedActiveFileWithTokenAsync();
        SetupObjectExists();
        SetupPresignedDownloadUrl("https://mock-s3.test/download-despite-notifier-failure");
        _notifierMock
            .Setup(n => n.NotifyDownloadAsync(It.IsAny<Guid>(), It.IsAny<FileDownloadedNotification>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated SignalR outage"));

        var result = await _sut.DownloadAsync(accessToken, "203.0.113.10", "TestAgent/1.0");

        Assert.True(result.IsSuccess);
        Assert.Equal("https://mock-s3.test/download-despite-notifier-failure", result.Value!.DownloadUrl);
    }

    [Fact]
    public async Task DownloadAsync_NotifierFailure_DoesNotRollBackTheAlreadyPersistedDownload()
    {
        var (fileId, _, _, accessToken) = await SeedActiveFileWithTokenAsync();
        SetupObjectExists();
        SetupPresignedDownloadUrl();
        _notifierMock
            .Setup(n => n.NotifyDownloadAsync(It.IsAny<Guid>(), It.IsAny<FileDownloadedNotification>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated SignalR outage"));

        await _sut.DownloadAsync(accessToken, "203.0.113.10", "TestAgent/1.0");

        Assert.True(await _dbContext.Downloads.AnyAsync(d => d.FileId == fileId));
    }
}
