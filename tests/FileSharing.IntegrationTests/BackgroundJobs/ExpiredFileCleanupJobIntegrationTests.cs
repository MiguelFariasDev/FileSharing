using System.Net.Http.Headers;
using Amazon;
using Amazon.S3;
using FileSharing.Application.Abstractions.Storage;
using FileSharing.Infrastructure.BackgroundJobs;
using FileSharing.Infrastructure.Persistence;
using FileSharing.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using CompressionType = FileSharing.Domain.Enums.CompressionType;
using FileStatus = FileSharing.Domain.Enums.FileStatus;
using File = FileSharing.Domain.Entities.File;

namespace FileSharing.IntegrationTests.BackgroundJobs;

/// <summary>
/// Exercises the Phase 6 expiration cleanup job (locate expired Active files -> delete the S3
/// object -> mark Expired -> persist) against real infrastructure — PostgreSQL and LocalStack S3
/// — instead of the InMemory DB + mocked storage used by FileSharing.UnitTests. Requires the
/// stack from infrastructure/docker/docker-compose.yml to be running:
///
///   cd infrastructure/docker
///   docker compose up -d
///   dotnet test tests/FileSharing.IntegrationTests
///
/// Instantiates ExpiredFileCleanupJob directly against a real ApplicationDbContext and a real
/// S3FileStorageService — the same pattern already established by
/// FileDownloadIntegrationTests/S3FileStorageServiceTests — never through Hangfire's scheduler,
/// consistent with CLAUDE.md's "test the job's Execute method directly as a plain class".
///
/// Never talks to real AWS: the endpoint, bucket and credentials below are all LocalStack-only
/// development values, matching src/FileSharing.Api/appsettings.Development.json.
/// </summary>
public class ExpiredFileCleanupJobIntegrationTests : IAsyncLifetime
{
    private const string BucketName = "filesharing-dev";
    private const string LocalStackEndpoint = "http://localhost:4566";

    private const string PostgresConnectionString =
        "Host=localhost;Port=5433;Database=filesharing;Username=postgres;Password=postgres";

    private readonly ApplicationDbContext _dbContext;
    private readonly AmazonS3Client _s3Client;
    private readonly S3FileStorageService _storageService;
    private readonly HttpClient _httpClient = new();

    private readonly List<Guid> _fileIdsToCleanUp = [];
    private readonly List<Guid> _userIdsToCleanUp = [];
    private readonly List<string> _storageKeysToCleanUp = [];

    public ExpiredFileCleanupJobIntegrationTests()
    {
        Environment.SetEnvironmentVariable("AWS_ENDPOINT_URL_S3", LocalStackEndpoint);

        var s3Config = new AmazonS3Config
        {
            ServiceURL = LocalStackEndpoint,
            ForcePathStyle = true,
            RegionEndpoint = RegionEndpoint.USEast1
        };
        _s3Client = new AmazonS3Client("test", "test", s3Config);

        var storageOptions = Options.Create(new FileStorageOptions
        {
            BucketName = BucketName,
            Region = "us-east-1",
            PresignedUploadExpirationMinutes = 15,
            DownloadUrlExpirationSeconds = 300
        });
        _storageService = new S3FileStorageService(_s3Client, storageOptions);

        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(PostgresConnectionString)
            .Options;
        _dbContext = new ApplicationDbContext(dbOptions);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var key in _storageKeysToCleanUp)
            await _storageService.DeleteObjectAsync(key);

        var files = await _dbContext.Files.Where(f => _fileIdsToCleanUp.Contains(f.Id)).ToListAsync();
        _dbContext.Files.RemoveRange(files);

        var users = await _dbContext.Users.Where(u => _userIdsToCleanUp.Contains(u.Id)).ToListAsync();
        _dbContext.Users.RemoveRange(users);

        await _dbContext.SaveChangesAsync();

        _dbContext.Dispose();
        _s3Client.Dispose();
        _httpClient.Dispose();
    }

    private ExpiredFileCleanupJob CreateSut(int batchSize = 100) =>
        new(_dbContext, _storageService, Options.Create(new ExpirationCleanupOptions { BatchSize = batchSize }), NullLogger<ExpiredFileCleanupJob>.Instance);

    private async Task<File> SeedActiveFileAsync(DateTimeOffset completedAtUtc, bool putRealObject)
    {
        var storageKey = $"integration-tests/cleanup-{Guid.NewGuid():N}";
        var user = new FileSharing.Domain.Entities.User($"integration-{Guid.NewGuid():N}@example.com", "dummy-hash");
        var file = new File(user.Id, "document.pdf", storageKey, "application/pdf", 1024, false, CompressionType.None);
        file.CompleteUpload(completedAtUtc);

        _dbContext.Users.Add(user);
        _dbContext.Files.Add(file);
        await _dbContext.SaveChangesAsync();

        _userIdsToCleanUp.Add(user.Id);
        _fileIdsToCleanUp.Add(file.Id);
        _storageKeysToCleanUp.Add(storageKey);

        if (putRealObject)
        {
            var presignedUpload = await _storageService.CreatePresignedUploadUrlAsync(storageKey, "application/pdf");
            using var content = new ByteArrayContent("%PDF-1.4 fake content for cleanup integration test"u8.ToArray());
            content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            var putResponse = await _httpClient.PutAsync(presignedUpload.Url, content);
            putResponse.EnsureSuccessStatusCode();
        }

        return file;
    }

    [Fact]
    public async Task ExecuteAsync_ActiveExpiredFileWithRealObject_DeletesTheObject_AndMarksFileExpired()
    {
        var file = await SeedActiveFileAsync(DateTimeOffset.UtcNow.AddHours(-25), putRealObject: true);
        Assert.True(await _storageService.ObjectExistsAsync(file.StorageKey));

        var result = await CreateSut().ExecuteAsync();

        Assert.True(result.Expired >= 1);
        Assert.False(await _storageService.ObjectExistsAsync(file.StorageKey));

        var reloaded = await _dbContext.Files.SingleAsync(f => f.Id == file.Id);
        Assert.Equal(FileStatus.Expired, reloaded.Status);
    }

    [Fact]
    public async Task ExecuteAsync_ActiveExpiredFileWithNoObjectInStorage_StillMarksFileExpired()
    {
        var file = await SeedActiveFileAsync(DateTimeOffset.UtcNow.AddHours(-25), putRealObject: false);

        var result = await CreateSut().ExecuteAsync();

        Assert.True(result.Expired >= 1);
        var reloaded = await _dbContext.Files.SingleAsync(f => f.Id == file.Id);
        Assert.Equal(FileStatus.Expired, reloaded.Status);
    }

    [Fact]
    public async Task ExecuteAsync_ActiveFileStillValid_LeavesObjectAndFileUntouched()
    {
        var file = await SeedActiveFileAsync(DateTimeOffset.UtcNow, putRealObject: true);

        await CreateSut().ExecuteAsync();

        Assert.True(await _storageService.ObjectExistsAsync(file.StorageKey));
        var reloaded = await _dbContext.Files.SingleAsync(f => f.Id == file.Id);
        Assert.Equal(FileStatus.Active, reloaded.Status);
    }

    [Fact]
    public async Task ExecuteAsync_CalledTwice_SecondRunIsANoOp_AndStateStaysConsistent()
    {
        var file = await SeedActiveFileAsync(DateTimeOffset.UtcNow.AddHours(-25), putRealObject: true);

        var first = await CreateSut().ExecuteAsync();
        Assert.True(first.Expired >= 1);
        Assert.False(await _storageService.ObjectExistsAsync(file.StorageKey));

        var second = await CreateSut().ExecuteAsync();

        Assert.Equal(0, second.Failed);
        var reloaded = await _dbContext.Files.SingleAsync(f => f.Id == file.Id);
        Assert.Equal(FileStatus.Expired, reloaded.Status);
        Assert.False(await _storageService.ObjectExistsAsync(file.StorageKey));
    }
}
