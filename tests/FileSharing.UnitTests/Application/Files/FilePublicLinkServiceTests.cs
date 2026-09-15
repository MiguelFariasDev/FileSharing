using FileSharing.Application.Common;
using FileSharing.Application.Common.Exceptions;
using FileSharing.Application.Observability;
using FileSharing.Application.Services.Files;
using FileSharing.Domain.Enums;
using FileSharing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileSharing.UnitTests.Application.Files;

public class FilePublicLinkServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly FilePublicLinkService _sut;

    public FilePublicLinkServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _sut = new FilePublicLinkService(_dbContext, new AppMetrics(), NullLogger<FilePublicLinkService>.Instance);
    }

    public void Dispose() => _dbContext.Dispose();

    private async Task<Guid> SeedActiveFileAsync(Guid userId, DateTimeOffset? completedAt = null)
    {
        var file = new FileSharing.Domain.Entities.File(userId, "document.pdf", "storage-key", "application/pdf", 1024, false, CompressionType.None);
        file.CompleteUpload(completedAt ?? DateTimeOffset.UtcNow);
        _dbContext.Files.Add(file);
        await _dbContext.SaveChangesAsync();
        return file.Id;
    }

    private async Task<Guid> SeedPendingFileAsync(Guid userId)
    {
        var file = new FileSharing.Domain.Entities.File(userId, "document.pdf", "storage-key", "application/pdf", 1024, false, CompressionType.None);
        _dbContext.Files.Add(file);
        await _dbContext.SaveChangesAsync();
        return file.Id;
    }

    [Fact]
    public async Task GenerateLinkAsync_Succeeds_ForOwnerOfActiveFile()
    {
        var userId = Guid.NewGuid();
        var fileId = await SeedActiveFileAsync(userId);

        var response = await _sut.GenerateLinkAsync(userId, fileId);

        Assert.Equal(fileId, response.FileId);
        Assert.False(string.IsNullOrWhiteSpace(response.AccessToken));
    }

    [Fact]
    public async Task GenerateLinkAsync_PersistsOnlyTheHash_NeverThePlaintextToken()
    {
        var userId = Guid.NewGuid();
        var fileId = await SeedActiveFileAsync(userId);

        var response = await _sut.GenerateLinkAsync(userId, fileId);

        var file = await _dbContext.Files.SingleAsync(f => f.Id == fileId);
        Assert.Equal(AccessTokenHasher.Hash(response.AccessToken), file.AccessTokenHash);
        Assert.NotEqual(response.AccessToken, file.AccessTokenHash);
    }

    [Fact]
    public async Task GenerateLinkAsync_ProducesDifferentTokens_OnEachCall()
    {
        var userId = Guid.NewGuid();
        var fileId = await SeedActiveFileAsync(userId);

        var first = await _sut.GenerateLinkAsync(userId, fileId);
        var second = await _sut.GenerateLinkAsync(userId, fileId);

        Assert.NotEqual(first.AccessToken, second.AccessToken);
    }

    [Fact]
    public async Task GenerateLinkAsync_Fails_WhenFileBelongsToAnotherUser()
    {
        var owner = Guid.NewGuid();
        var fileId = await SeedActiveFileAsync(owner);

        var exception = await Assert.ThrowsAsync<ResourceNotFoundException>(
            () => _sut.GenerateLinkAsync(Guid.NewGuid(), fileId));

        Assert.Equal("FILE_NOT_FOUND", exception.PublicCode);
    }

    [Fact]
    public async Task GenerateLinkAsync_Fails_WhenFileDoesNotExist()
    {
        var exception = await Assert.ThrowsAsync<ResourceNotFoundException>(
            () => _sut.GenerateLinkAsync(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal("FILE_NOT_FOUND", exception.PublicCode);
    }

    [Fact]
    public async Task GenerateLinkAsync_Fails_WhenUploadIsStillPending()
    {
        var userId = Guid.NewGuid();
        var fileId = await SeedPendingFileAsync(userId);

        var exception = await Assert.ThrowsAsync<ConflictException>(() => _sut.GenerateLinkAsync(userId, fileId));

        Assert.Equal("FILE_UPLOAD_NOT_COMPLETED", exception.PublicCode);
    }

    [Fact]
    public async Task GenerateLinkAsync_Fails_WhenFileIsAlreadyExpired()
    {
        var userId = Guid.NewGuid();
        var fileId = await SeedActiveFileAsync(userId, completedAt: DateTimeOffset.UtcNow.AddHours(-25));

        var exception = await Assert.ThrowsAsync<ConflictException>(() => _sut.GenerateLinkAsync(userId, fileId));

        Assert.Equal("FILE_EXPIRED", exception.PublicCode);
    }

    [Fact]
    public async Task GetByAccessTokenAsync_Succeeds_ForAValidToken()
    {
        var userId = Guid.NewGuid();
        var fileId = await SeedActiveFileAsync(userId);
        var generated = await _sut.GenerateLinkAsync(userId, fileId);

        var response = await _sut.GetByAccessTokenAsync(generated.AccessToken);

        Assert.Equal(fileId, response.FileId);
    }

    [Fact]
    public async Task GetByAccessTokenAsync_Throws_ForAnUnknownToken()
    {
        var exception = await Assert.ThrowsAsync<ResourceNotFoundException>(
            () => _sut.GetByAccessTokenAsync("token-that-was-never-issued"));

        Assert.Equal("FILE_NOT_FOUND", exception.PublicCode);
    }

    [Fact]
    public async Task GetByAccessTokenAsync_Throws_ForAnExpiredFilesToken()
    {
        var userId = Guid.NewGuid();
        var fileId = await SeedActiveFileAsync(userId);
        var generated = await _sut.GenerateLinkAsync(userId, fileId);

        var file = await _dbContext.Files.SingleAsync(f => f.Id == fileId);
        _dbContext.Entry(file).Property("ExpiresAt").CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-1);
        await _dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => _sut.GetByAccessTokenAsync(generated.AccessToken));
    }

    [Fact]
    public async Task GetByAccessTokenAsync_UnknownAndExpiredTokens_ProduceTheSameOutcomeShape()
    {
        var userId = Guid.NewGuid();
        var fileId = await SeedActiveFileAsync(userId);
        var generated = await _sut.GenerateLinkAsync(userId, fileId);

        var file = await _dbContext.Files.SingleAsync(f => f.Id == fileId);
        _dbContext.Entry(file).Property("ExpiresAt").CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-1);
        await _dbContext.SaveChangesAsync();

        var expiredException = await Assert.ThrowsAsync<ResourceNotFoundException>(() => _sut.GetByAccessTokenAsync(generated.AccessToken));
        var unknownException = await Assert.ThrowsAsync<ResourceNotFoundException>(() => _sut.GetByAccessTokenAsync("some-token-that-does-not-exist"));

        Assert.Equal(expiredException.PublicCode, unknownException.PublicCode);
        Assert.Equal(expiredException.PublicMessage, unknownException.PublicMessage);
    }
}
