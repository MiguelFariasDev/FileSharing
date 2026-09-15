using FileSharing.Application.Common;
using FileSharing.Application.Common.Exceptions;
using FileSharing.Application.Services.Files;
using FileSharing.Domain.Enums;
using FileSharing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using CompressionType = FileSharing.Domain.Enums.CompressionType;
using File = FileSharing.Domain.Entities.File;
using User = FileSharing.Domain.Entities.User;

namespace FileSharing.UnitTests.Application.Files;

public class FileQueryServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly FileQueryService _sut;

    public FileQueryServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _sut = new FileQueryService(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    private async Task<Guid> SeedUserAsync()
    {
        var user = new User($"user-{Guid.NewGuid():N}@example.com", "hash");
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
        return user.Id;
    }

    private async Task<File> SeedFileAsync(Guid userId, FileStatus status, bool assignLink = false)
    {
        var file = new File(userId, "document.pdf", $"key-{Guid.NewGuid():N}", "application/pdf", 2048, false, CompressionType.None);

        if (status is FileStatus.Active or FileStatus.Expired)
        {
            var completedAt = status == FileStatus.Expired ? DateTimeOffset.UtcNow.AddHours(-25) : DateTimeOffset.UtcNow;
            file.CompleteUpload(completedAt);

            if (assignLink)
                file.AssignAccessToken("some-hash", completedAt);

            if (status == FileStatus.Expired)
                file.MarkAsExpired();
        }

        _dbContext.Files.Add(file);
        await _dbContext.SaveChangesAsync();
        return file;
    }

    [Fact]
    public async Task GetMyFilesAsync_ReturnsOnlyFilesOwnedByTheGivenUser()
    {
        var userId = await SeedUserAsync();
        var otherUserId = await SeedUserAsync();
        await SeedFileAsync(userId, FileStatus.Active);
        await SeedFileAsync(otherUserId, FileStatus.Active);

        var result = await _sut.GetMyFilesAsync(userId);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetMyFilesAsync_UserWithNoFiles_ReturnsEmptyList()
    {
        var userId = await SeedUserAsync();

        var result = await _sut.GetMyFilesAsync(userId);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(FileStatus.PendingUpload, "PendingUpload")]
    [InlineData(FileStatus.Active, "Active")]
    [InlineData(FileStatus.Expired, "Expired")]
    public async Task GetMyFilesAsync_ReportsStatusAsString(FileStatus status, string expected)
    {
        var userId = await SeedUserAsync();
        await SeedFileAsync(userId, status);

        var result = await _sut.GetMyFilesAsync(userId);

        Assert.Equal(expected, result.Single().Status);
    }

    [Fact]
    public async Task GetMyFilesAsync_ReflectsWhetherAPublicLinkExists()
    {
        var userId = await SeedUserAsync();
        await SeedFileAsync(userId, FileStatus.Active, assignLink: true);

        var result = await _sut.GetMyFilesAsync(userId);

        Assert.True(result.Single().HasPublicLink);
    }

    [Fact]
    public async Task GetMyFilesAsync_WithoutALink_HasPublicLinkIsFalse()
    {
        var userId = await SeedUserAsync();
        await SeedFileAsync(userId, FileStatus.Active, assignLink: false);

        var result = await _sut.GetMyFilesAsync(userId);

        Assert.False(result.Single().HasPublicLink);
    }

    [Fact]
    public async Task GetMyFilesAsync_CountsDownloadsCorrectly()
    {
        var userId = await SeedUserAsync();
        var file = await SeedFileAsync(userId, FileStatus.Active);
        _dbContext.Downloads.Add(new FileSharing.Domain.Entities.Download(file.Id, "203.0.113.10", "TestAgent/1.0"));
        _dbContext.Downloads.Add(new FileSharing.Domain.Entities.Download(file.Id, "203.0.113.11", "TestAgent/1.0"));
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetMyFilesAsync(userId);

        Assert.Equal(2, result.Single().DownloadCount);
    }

    [Fact]
    public async Task GetMyFilesAsync_PendingUploadFile_HasNoCreatedOrExpiresAt()
    {
        var userId = await SeedUserAsync();
        await SeedFileAsync(userId, FileStatus.PendingUpload);

        var result = await _sut.GetMyFilesAsync(userId);

        Assert.Null(result.Single().CreatedAt);
        Assert.Null(result.Single().ExpiresAt);
    }

    [Fact]
    public async Task GetDownloadHistoryAsync_OwnFile_ReturnsHistoryOrderedNewestFirst()
    {
        var userId = await SeedUserAsync();
        var file = await SeedFileAsync(userId, FileStatus.Active);
        var older = new FileSharing.Domain.Entities.Download(file.Id, "203.0.113.10", "TestAgent/1.0");
        _dbContext.Downloads.Add(older);
        await _dbContext.SaveChangesAsync();
        await Task.Delay(5);
        var newer = new FileSharing.Domain.Entities.Download(file.Id, "203.0.113.11", "TestAgent/1.0");
        _dbContext.Downloads.Add(newer);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetDownloadHistoryAsync(userId, file.Id);

        Assert.Equal(2, result.Count);
        Assert.Equal(newer.DownloadedAt, result[0].DownloadedAt);
        Assert.Equal(older.DownloadedAt, result[1].DownloadedAt);
    }

    [Fact]
    public async Task GetDownloadHistoryAsync_NeverExposesIpOrUserAgent()
    {
        // Structural guard: DownloadHistoryEntryResponse must not gain an IpAddress/UserAgent
        // field without someone consciously deciding it belongs on the dashboard.
        var properties = typeof(FileSharing.Application.DTOs.Files.DownloadHistoryEntryResponse).GetProperties().Select(p => p.Name);

        Assert.Equal(new[] { "DownloadedAt" }, properties);
    }

    [Fact]
    public async Task GetDownloadHistoryAsync_NonexistentFile_ReturnsFailure()
    {
        var userId = await SeedUserAsync();

        var exception = await Assert.ThrowsAsync<ResourceNotFoundException>(
            () => _sut.GetDownloadHistoryAsync(userId, Guid.NewGuid()));

        Assert.Equal("FILE_NOT_FOUND", exception.PublicCode);
    }

    [Fact]
    public async Task GetDownloadHistoryAsync_FileOfAnotherUser_ReturnsFailure_NeverTheData()
    {
        var ownerUserId = await SeedUserAsync();
        var otherUserId = await SeedUserAsync();
        var file = await SeedFileAsync(ownerUserId, FileStatus.Active);
        _dbContext.Downloads.Add(new FileSharing.Domain.Entities.Download(file.Id, "203.0.113.10", "TestAgent/1.0"));
        await _dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => _sut.GetDownloadHistoryAsync(otherUserId, file.Id));
    }
}
