using FileSharing.Application.Common;
using FileSharing.Application.Services.Files;
using FileSharing.Domain.Enums;
using FileSharing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
        _sut = new FilePublicLinkService(_dbContext);
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

        var result = await _sut.GenerateLinkAsync(userId, fileId);

        Assert.True(result.IsSuccess);
        Assert.Equal(fileId, result.Value!.FileId);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.AccessToken));
    }

    [Fact]
    public async Task GenerateLinkAsync_PersistsOnlyTheHash_NeverThePlaintextToken()
    {
        var userId = Guid.NewGuid();
        var fileId = await SeedActiveFileAsync(userId);

        var result = await _sut.GenerateLinkAsync(userId, fileId);

        var file = await _dbContext.Files.SingleAsync(f => f.Id == fileId);
        Assert.Equal(AccessTokenHasher.Hash(result.Value!.AccessToken), file.AccessTokenHash);
        Assert.NotEqual(result.Value.AccessToken, file.AccessTokenHash);
    }

    [Fact]
    public async Task GenerateLinkAsync_ProducesDifferentTokens_OnEachCall()
    {
        var userId = Guid.NewGuid();
        var fileId = await SeedActiveFileAsync(userId);

        var first = await _sut.GenerateLinkAsync(userId, fileId);
        var second = await _sut.GenerateLinkAsync(userId, fileId);

        Assert.NotEqual(first.Value!.AccessToken, second.Value!.AccessToken);
    }

    [Fact]
    public async Task GenerateLinkAsync_Fails_WhenFileBelongsToAnotherUser()
    {
        var owner = Guid.NewGuid();
        var fileId = await SeedActiveFileAsync(owner);

        var result = await _sut.GenerateLinkAsync(Guid.NewGuid(), fileId);

        Assert.False(result.IsSuccess);
        Assert.Equal(GenerateLinkFailureReason.NotFound, result.FailureReason);
    }

    [Fact]
    public async Task GenerateLinkAsync_Fails_WhenFileDoesNotExist()
    {
        var result = await _sut.GenerateLinkAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal(GenerateLinkFailureReason.NotFound, result.FailureReason);
    }

    [Fact]
    public async Task GenerateLinkAsync_Fails_WhenUploadIsStillPending()
    {
        var userId = Guid.NewGuid();
        var fileId = await SeedPendingFileAsync(userId);

        var result = await _sut.GenerateLinkAsync(userId, fileId);

        Assert.False(result.IsSuccess);
        Assert.Equal(GenerateLinkFailureReason.Conflict, result.FailureReason);
    }

    [Fact]
    public async Task GenerateLinkAsync_Fails_WhenFileIsAlreadyExpired()
    {
        var userId = Guid.NewGuid();
        var fileId = await SeedActiveFileAsync(userId, completedAt: DateTimeOffset.UtcNow.AddHours(-25));

        var result = await _sut.GenerateLinkAsync(userId, fileId);

        Assert.False(result.IsSuccess);
        Assert.Equal(GenerateLinkFailureReason.Conflict, result.FailureReason);
    }

    [Fact]
    public async Task GetByAccessTokenAsync_Succeeds_ForAValidToken()
    {
        var userId = Guid.NewGuid();
        var fileId = await SeedActiveFileAsync(userId);
        var generated = await _sut.GenerateLinkAsync(userId, fileId);

        var result = await _sut.GetByAccessTokenAsync(generated.Value!.AccessToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(fileId, result.Value!.FileId);
    }

    [Fact]
    public async Task GetByAccessTokenAsync_ReturnsNotAvailable_ForAnUnknownToken()
    {
        var result = await _sut.GetByAccessTokenAsync("token-that-was-never-issued");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task GetByAccessTokenAsync_ReturnsNotAvailable_ForAnExpiredFilesToken()
    {
        var userId = Guid.NewGuid();
        var fileId = await SeedActiveFileAsync(userId);
        var generated = await _sut.GenerateLinkAsync(userId, fileId);

        var file = await _dbContext.Files.SingleAsync(f => f.Id == fileId);
        _dbContext.Entry(file).Property("ExpiresAt").CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-1);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetByAccessTokenAsync(generated.Value!.AccessToken);

        Assert.False(result.IsSuccess);
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

        var expiredResult = await _sut.GetByAccessTokenAsync(generated.Value!.AccessToken);
        var unknownResult = await _sut.GetByAccessTokenAsync("some-token-that-does-not-exist");

        Assert.Equal(expiredResult.IsSuccess, unknownResult.IsSuccess);
        Assert.Equal(expiredResult.Value, unknownResult.Value);
    }
}
