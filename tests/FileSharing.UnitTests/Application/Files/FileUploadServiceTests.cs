using FileSharing.Application.Abstractions.Storage;
using FileSharing.Application.Common.Exceptions;
using FileSharing.Application.DTOs.Files;
using FileSharing.Application.Observability;
using FileSharing.Application.Services.Files;
using FileSharing.Domain.Enums;
using FileSharing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FileSharing.UnitTests.Application.Files;

public class FileUploadServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IFileStorageService> _storageMock = new();
    private readonly FileUploadService _sut;

    public FileUploadServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _sut = new FileUploadService(_dbContext, _storageMock.Object, new AppMetrics(), NullLogger<FileUploadService>.Instance);
    }

    public void Dispose() => _dbContext.Dispose();

    private static InitiateUploadRequest CreatePdfRequest() =>
        new("document.pdf", "application/pdf", 1024, false);

    [Fact]
    public async Task InitiateUploadAsync_CreatesFileAsPendingUpload()
    {
        _storageMock
            .Setup(s => s.CreatePresignedUploadUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PresignedUploadUrl("https://mock/upload", DateTimeOffset.UtcNow.AddMinutes(15)));

        var userId = Guid.NewGuid();
        var response = await _sut.InitiateUploadAsync(userId, CreatePdfRequest());

        var file = await _dbContext.Files.SingleAsync(f => f.Id == response.FileId);
        Assert.Equal(FileStatus.PendingUpload, file.Status);
        Assert.False(file.IsActive);
        Assert.Null(file.CreatedAt);
        Assert.Null(file.ExpiresAt);
    }

    [Fact]
    public async Task InitiateUploadAsync_GeneratesUnpredictableStorageKey()
    {
        _storageMock
            .Setup(s => s.CreatePresignedUploadUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PresignedUploadUrl("https://mock/upload", DateTimeOffset.UtcNow.AddMinutes(15)));

        var userId = Guid.NewGuid();
        var first = await _sut.InitiateUploadAsync(userId, CreatePdfRequest());
        var second = await _sut.InitiateUploadAsync(userId, CreatePdfRequest());

        var firstFile = await _dbContext.Files.SingleAsync(f => f.Id == first.FileId);
        var secondFile = await _dbContext.Files.SingleAsync(f => f.Id == second.FileId);

        Assert.NotEqual(firstFile.StorageKey, secondFile.StorageKey);
        Assert.DoesNotContain("document", firstFile.StorageKey, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".pdf", firstFile.StorageKey, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InitiateUploadAsync_ReturnsThePresignedUrlFromStorage()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
        _storageMock
            .Setup(s => s.CreatePresignedUploadUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PresignedUploadUrl("https://mock/upload-here", expiresAt));

        var response = await _sut.InitiateUploadAsync(Guid.NewGuid(), CreatePdfRequest());

        Assert.Equal("https://mock/upload-here", response.UploadUrl);
        Assert.Equal(expiresAt, response.ExpiresAt);
    }

    [Fact]
    public async Task CompleteUploadAsync_Fails_WhenFileBelongsToAnotherUser()
    {
        var owner = Guid.NewGuid();
        var fileId = await SeedPendingFileAsync(owner, sizeBytes: 1024, contentType: "application/pdf");

        var exception = await Assert.ThrowsAsync<ResourceNotFoundException>(
            () => _sut.CompleteUploadAsync(Guid.NewGuid(), fileId));

        Assert.Equal("FILE_NOT_FOUND", exception.PublicCode);
    }

    [Fact]
    public async Task CompleteUploadAsync_Fails_WhenObjectMissingInStorage()
    {
        var userId = Guid.NewGuid();
        var fileId = await SeedPendingFileAsync(userId, sizeBytes: 1024, contentType: "application/pdf");

        _storageMock
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StorageObjectMetadata?)null);

        var exception = await Assert.ThrowsAsync<ConflictException>(() => _sut.CompleteUploadAsync(userId, fileId));

        Assert.Equal("FILE_UPLOAD_INVALID_STATE", exception.PublicCode);

        var file = await _dbContext.Files.SingleAsync(f => f.Id == fileId);
        Assert.Equal(FileStatus.PendingUpload, file.Status);
    }

    [Fact]
    public async Task CompleteUploadAsync_Fails_WhenSizeDoesNotMatch()
    {
        var userId = Guid.NewGuid();
        var fileId = await SeedPendingFileAsync(userId, sizeBytes: 1024, contentType: "application/pdf");

        _storageMock
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageObjectMetadata(999, "application/pdf"));

        var exception = await Assert.ThrowsAsync<ConflictException>(() => _sut.CompleteUploadAsync(userId, fileId));

        Assert.Equal("FILE_UPLOAD_INVALID_STATE", exception.PublicCode);

        var file = await _dbContext.Files.SingleAsync(f => f.Id == fileId);
        Assert.Equal(FileStatus.PendingUpload, file.Status);
    }

    [Fact]
    public async Task CompleteUploadAsync_TransitionsToActive_AndSetsThe24HourWindow_OnSuccess()
    {
        var userId = Guid.NewGuid();
        var fileId = await SeedPendingFileAsync(userId, sizeBytes: 1024, contentType: "application/pdf");

        _storageMock
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageObjectMetadata(1024, "application/pdf"));

        var before = DateTimeOffset.UtcNow;
        await _sut.CompleteUploadAsync(userId, fileId);
        var after = DateTimeOffset.UtcNow;

        var file = await _dbContext.Files.SingleAsync(f => f.Id == fileId);
        Assert.Equal(FileStatus.Active, file.Status);
        Assert.InRange(file.CreatedAt!.Value, before, after);
        Assert.Equal(file.CreatedAt.Value.AddHours(24), file.ExpiresAt);
    }

    [Fact]
    public async Task CompleteUploadAsync_Fails_WhenAlreadyCompleted()
    {
        var userId = Guid.NewGuid();
        var fileId = await SeedPendingFileAsync(userId, sizeBytes: 1024, contentType: "application/pdf");

        _storageMock
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageObjectMetadata(1024, "application/pdf"));

        await _sut.CompleteUploadAsync(userId, fileId);

        var exception = await Assert.ThrowsAsync<ConflictException>(() => _sut.CompleteUploadAsync(userId, fileId));
        Assert.Equal("FILE_UPLOAD_INVALID_STATE", exception.PublicCode);
    }

    private async Task<Guid> SeedPendingFileAsync(Guid userId, long sizeBytes, string contentType)
    {
        var file = new FileSharing.Domain.Entities.File(userId, "document.pdf", "storage-key", contentType, sizeBytes, false, CompressionType.None);
        _dbContext.Files.Add(file);
        await _dbContext.SaveChangesAsync();
        return file.Id;
    }
}
