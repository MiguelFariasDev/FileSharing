using System.Reflection;
using FileSharing.Application.Abstractions.Storage;
using FileSharing.Domain.Enums;
using FileSharing.Infrastructure.BackgroundJobs;
using FileSharing.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using CompressionType = FileSharing.Domain.Enums.CompressionType;
using File = FileSharing.Domain.Entities.File;

namespace FileSharing.UnitTests.Infrastructure.BackgroundJobs;

public class ExpiredFileCleanupJobTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IFileStorageService> _storageMock = new();
    private ExpirationCleanupOptions _options = new();

    public ExpiredFileCleanupJobTests()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(dbOptions);

        _storageMock
            .Setup(s => s.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    public void Dispose() => _dbContext.Dispose();

    private ExpiredFileCleanupJob CreateSut() =>
        new(_dbContext, _storageMock.Object, Options.Create(_options), NullLogger<ExpiredFileCleanupJob>.Instance);

    private async Task<File> SeedFileAsync(FileStatus status, DateTimeOffset? completedAt = null)
    {
        var user = new FileSharing.Domain.Entities.User($"user-{Guid.NewGuid():N}@example.com", "hash");
        var file = new File(user.Id, "document.pdf", $"storage-key-{Guid.NewGuid():N}", "application/pdf", 1024, false, CompressionType.None);

        _dbContext.Users.Add(user);
        _dbContext.Files.Add(file);

        if (status is FileStatus.Active or FileStatus.Expired)
        {
            file.CompleteUpload(completedAt ?? DateTimeOffset.UtcNow);

            if (status is FileStatus.Expired)
                file.MarkAsExpired();
        }

        await _dbContext.SaveChangesAsync();
        return file;
    }

    [Fact]
    public async Task ExecuteAsync_ActiveAndExpiredFile_DeletesFromStorage_AndMarksExpired()
    {
        var file = await SeedFileAsync(FileStatus.Active, completedAt: DateTimeOffset.UtcNow.AddHours(-25));

        var result = await CreateSut().ExecuteAsync();

        _storageMock.Verify(s => s.DeleteObjectAsync(file.StorageKey, It.IsAny<CancellationToken>()), Times.Once);
        var reloaded = await _dbContext.Files.SingleAsync(f => f.Id == file.Id);
        Assert.Equal(FileStatus.Expired, reloaded.Status);
        Assert.Equal(1, result.Expired);
        Assert.Equal(0, result.Failed);
        Assert.Equal(1, result.CandidatesFound);
    }

    [Fact]
    public async Task ExecuteAsync_ActiveAndStillValidFile_DoesNotDeleteFromStorage_AndStaysActive()
    {
        var file = await SeedFileAsync(FileStatus.Active, completedAt: DateTimeOffset.UtcNow);

        var result = await CreateSut().ExecuteAsync();

        _storageMock.Verify(s => s.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        var reloaded = await _dbContext.Files.SingleAsync(f => f.Id == file.Id);
        Assert.Equal(FileStatus.Active, reloaded.Status);
        Assert.Equal(0, result.CandidatesFound);
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyExpiredFile_IsNotReprocessed()
    {
        var file = await SeedFileAsync(FileStatus.Expired, completedAt: DateTimeOffset.UtcNow.AddHours(-25));

        var result = await CreateSut().ExecuteAsync();

        _storageMock.Verify(s => s.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(0, result.CandidatesFound);
        var reloaded = await _dbContext.Files.SingleAsync(f => f.Id == file.Id);
        Assert.Equal(FileStatus.Expired, reloaded.Status);
    }

    [Fact]
    public async Task ExecuteAsync_PendingUploadFile_IsNeverACandidate()
    {
        await SeedFileAsync(FileStatus.PendingUpload);

        var result = await CreateSut().ExecuteAsync();

        Assert.Equal(0, result.CandidatesFound);
        _storageMock.Verify(s => s.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ObjectAlreadyMissingFromStorage_IsTreatedAsSuccessfulCleanup()
    {
        // S3's DeleteObject does not throw for a key that is already gone (real AWS/LocalStack
        // behavior) — simulated here simply by the mock's default non-throwing setup, which is
        // indistinguishable, by design, from "object still there and just removed".
        var file = await SeedFileAsync(FileStatus.Active, completedAt: DateTimeOffset.UtcNow.AddHours(-25));

        var result = await CreateSut().ExecuteAsync();

        Assert.Equal(1, result.Expired);
        var reloaded = await _dbContext.Files.SingleAsync(f => f.Id == file.Id);
        Assert.Equal(FileStatus.Expired, reloaded.Status);
    }

    [Fact]
    public async Task ExecuteAsync_StorageThrows_DoesNotMarkFileExpired_AndRecordsAFailure()
    {
        var file = await SeedFileAsync(FileStatus.Active, completedAt: DateTimeOffset.UtcNow.AddHours(-25));
        _storageMock
            .Setup(s => s.DeleteObjectAsync(file.StorageKey, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated transient S3 failure"));

        var result = await CreateSut().ExecuteAsync();

        Assert.Equal(0, result.Expired);
        Assert.Equal(1, result.Failed);
        var reloaded = await _dbContext.Files.SingleAsync(f => f.Id == file.Id);
        Assert.Equal(FileStatus.Active, reloaded.Status);
    }

    [Fact]
    public async Task ExecuteAsync_OneFileFailing_DoesNotPreventOthersFromBeingProcessed()
    {
        var failing = await SeedFileAsync(FileStatus.Active, completedAt: DateTimeOffset.UtcNow.AddHours(-25));
        var succeedingA = await SeedFileAsync(FileStatus.Active, completedAt: DateTimeOffset.UtcNow.AddHours(-26));
        var succeedingB = await SeedFileAsync(FileStatus.Active, completedAt: DateTimeOffset.UtcNow.AddHours(-27));

        _storageMock
            .Setup(s => s.DeleteObjectAsync(failing.StorageKey, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated transient S3 failure"));

        var result = await CreateSut().ExecuteAsync();

        Assert.Equal(3, result.CandidatesFound);
        Assert.Equal(2, result.Expired);
        Assert.Equal(1, result.Failed);

        Assert.Equal(FileStatus.Active, (await _dbContext.Files.SingleAsync(f => f.Id == failing.Id)).Status);
        Assert.Equal(FileStatus.Expired, (await _dbContext.Files.SingleAsync(f => f.Id == succeedingA.Id)).Status);
        Assert.Equal(FileStatus.Expired, (await _dbContext.Files.SingleAsync(f => f.Id == succeedingB.Id)).Status);
    }

    [Fact]
    public async Task ExecuteAsync_RespectsConfiguredBatchSize()
    {
        _options = new ExpirationCleanupOptions { BatchSize = 2 };
        for (var i = 0; i < 5; i++)
            await SeedFileAsync(FileStatus.Active, completedAt: DateTimeOffset.UtcNow.AddHours(-25 - i));

        var result = await CreateSut().ExecuteAsync();

        Assert.Equal(2, result.CandidatesFound);
        Assert.Equal(2, result.Expired);
        _storageMock.Verify(s => s.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        var stillActive = await _dbContext.Files.CountAsync(f => f.Status == FileStatus.Active);
        Assert.Equal(3, stillActive);
    }

    [Fact]
    public async Task ExecuteAsync_FileExpiringExactlyNow_IsTreatedAsExpired()
    {
        // ExpiresAt == DateTimeOffset.UtcNow is expired per File.IsExpired's own "asOfUtc >=
        // ExpiresAt" semantics; completing the upload exactly ExpirationHours ago pins
        // ExpiresAt to (approximately) "now" — any time elapsed between seeding and the job's
        // own UtcNow read only pushes further past the boundary, never short of it.
        var file = await SeedFileAsync(FileStatus.Active, completedAt: DateTimeOffset.UtcNow.AddHours(-File.ExpirationHours));

        var result = await CreateSut().ExecuteAsync();

        Assert.Equal(1, result.Expired);
        Assert.Equal(FileStatus.Expired, (await _dbContext.Files.SingleAsync(f => f.Id == file.Id)).Status);
    }

    [Fact]
    public void ExecuteAsync_DeclaresDisableConcurrentExecution_AndBoundedAutomaticRetry_OnTheInterfaceMethod()
    {
        // These attributes must live on the interface method, not the class's override —
        // Hangfire's job filter pipeline reads them off the MethodInfo captured by the
        // RecurringJob.AddOrUpdate<IExpiredFileCleanupJob> scheduling expression, which points
        // at the interface. See IExpiredFileCleanupJob's remarks.
        var method = typeof(IExpiredFileCleanupJob).GetMethod(nameof(IExpiredFileCleanupJob.ExecuteAsync))!;

        var disableConcurrent = method.GetCustomAttribute<DisableConcurrentExecutionAttribute>();
        Assert.NotNull(disableConcurrent);

        var retry = method.GetCustomAttribute<AutomaticRetryAttribute>();
        Assert.NotNull(retry);
        Assert.Equal(3, retry!.Attempts);
    }
}
