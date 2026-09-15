using System.Net.Http.Headers;
using Amazon;
using Amazon.S3;
using FileSharing.Application.Abstractions.Notifications;
using FileSharing.Application.Abstractions.Storage;
using FileSharing.Application.Common.Exceptions;
using FileSharing.Application.DTOs.Notifications;
using FileSharing.Application.Observability;
using FileSharing.Application.Services.Files;
using FileSharing.Domain.Entities;
using FileSharing.Infrastructure.BackgroundJobs;
using FileSharing.Infrastructure.Persistence;
using FileSharing.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using CompressionType = FileSharing.Domain.Enums.CompressionType;
using FileStatus = FileSharing.Domain.Enums.FileStatus;
using File = FileSharing.Domain.Entities.File;

namespace FileSharing.IntegrationTests.Files;

file sealed class NoOpFileDownloadNotifier : IFileDownloadNotifier
{
    public Task NotifyDownloadAsync(Guid ownerUserId, FileDownloadedNotification notification, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

/// <summary>
/// Fase 11: até agora, cada peça do fluxo (upload/S3, link público, download, cleanup do
/// Hangfire) era exercitada isoladamente contra infraestrutura real por um arquivo de teste
/// próprio (S3FileStorageServiceTests, FileDownloadIntegrationTests,
/// ExpiredFileCleanupJobIntegrationTests) — nenhum deles prova que a cadeia inteira compõe
/// corretamente de ponta a ponta. Este arquivo encadeia todas elas em uma única execução, contra
/// o mesmo Postgres/LocalStack reais, reaproveitando exatamente os mesmos serviços de Application
/// que a Api usa (nenhuma reimplementação, nenhum novo harness de HTTP — mesma decisão já
/// documentada em FileDownloadIntegrationTests de não duplicar o WebApplicationFactory que já
/// existe em FileSharing.ApiTests para isso).
///
///   cd infrastructure/docker
///   docker compose up -d
///   dotnet test tests/FileSharing.IntegrationTests
/// </summary>
public class FullLifecycleIntegrationTests : IAsyncLifetime
{
    private const string BucketName = "filesharing-dev";
    private const string LocalStackEndpoint = "http://localhost:4566";

    private const string PostgresConnectionString =
        "Host=localhost;Port=5433;Database=filesharing;Username=postgres;Password=postgres";

    private readonly ApplicationDbContext _dbContext;
    private readonly AmazonS3Client _s3Client;
    private readonly S3FileStorageService _storageService;
    private readonly FilePublicLinkService _linkService;
    private readonly FileDownloadService _downloadService;
    private readonly HttpClient _httpClient = new();

    private readonly List<Guid> _fileIdsToCleanUp = [];
    private readonly List<Guid> _userIdsToCleanUp = [];
    private readonly List<string> _storageKeysToCleanUp = [];

    public FullLifecycleIntegrationTests()
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
        _storageService = new S3FileStorageService(_s3Client, storageOptions, NullLogger<S3FileStorageService>.Instance);

        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(PostgresConnectionString)
            .Options;
        _dbContext = new ApplicationDbContext(dbOptions);

        _linkService = new FilePublicLinkService(_dbContext, new AppMetrics(), NullLogger<FilePublicLinkService>.Instance);
        _downloadService = new FileDownloadService(_dbContext, _storageService, new NoOpFileDownloadNotifier(), new AppMetrics(), NullLogger<FileDownloadService>.Instance);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var key in _storageKeysToCleanUp)
            await _storageService.DeleteObjectAsync(key);

        var files = await _dbContext.Files.Where(f => _fileIdsToCleanUp.Contains(f.Id)).ToListAsync();
        _dbContext.Files.RemoveRange(files);

        var downloads = await _dbContext.Downloads.Where(d => _fileIdsToCleanUp.Contains(d.FileId)).ToListAsync();
        _dbContext.Downloads.RemoveRange(downloads);

        var users = await _dbContext.Users.Where(u => _userIdsToCleanUp.Contains(u.Id)).ToListAsync();
        _dbContext.Users.RemoveRange(users);

        await _dbContext.SaveChangesAsync();

        _dbContext.Dispose();
        _s3Client.Dispose();
        _httpClient.Dispose();
    }

    [Fact]
    public async Task FullFlow_UploadThroughExpirationAndCleanup_StaysConsistentAtEveryStage()
    {
        // --- 1. Register (User entity directly — Application.Auth already covered by
        //        AuthEndpointsTests/JwtValidationTests against the real HTTP pipeline; this test's
        //        job is the file/storage/link/download/expiration chain, not auth itself). ---
        var user = new User($"integration-lifecycle-{Guid.NewGuid():N}@example.com", "dummy-hash");
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
        _userIdsToCleanUp.Add(user.Id);

        // --- 2. Create upload metadata + presigned PUT (mirrors FileUploadService.InitiateUploadAsync
        //        exactly: server-generated StorageKey, PendingUpload). ---
        var storageKey = $"integration-tests/lifecycle-{Guid.NewGuid():N}";
        var content = "%PDF-1.4 fake content for full lifecycle integration test"u8.ToArray();

        var file = new File(user.Id, "lifecycle-document.pdf", storageKey, "application/pdf", content.Length, false, CompressionType.None);
        _dbContext.Files.Add(file);
        await _dbContext.SaveChangesAsync();
        _fileIdsToCleanUp.Add(file.Id);
        _storageKeysToCleanUp.Add(storageKey);

        Assert.Equal(FileStatus.PendingUpload, file.Status);

        var presignedUpload = await _storageService.CreatePresignedUploadUrlAsync(storageKey, "application/pdf");

        // --- 3. Real PUT direct to LocalStack (no bytes ever flow through this test's "Api"). ---
        using (var putContent = new ByteArrayContent(content))
        {
            putContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            var putResponse = await _httpClient.PutAsync(presignedUpload.Url, putContent);
            putResponse.EnsureSuccessStatusCode();
        }

        // --- 4. Complete upload (mirrors FileUploadService.CompleteUploadAsync's own checks). ---
        var metadata = await _storageService.GetObjectMetadataAsync(storageKey);
        Assert.NotNull(metadata);
        Assert.Equal(content.Length, metadata!.SizeBytes);

        var completedAt = DateTimeOffset.UtcNow;
        file.CompleteUpload(completedAt);
        await _dbContext.SaveChangesAsync();

        // --- 5. File is Active, with the exact 24h expiration invariant. ---
        var afterComplete = await _dbContext.Files.SingleAsync(f => f.Id == file.Id);
        Assert.Equal(FileStatus.Active, afterComplete.Status);
        Assert.NotNull(afterComplete.CreatedAt);
        Assert.NotNull(afterComplete.ExpiresAt);
        Assert.Equal(afterComplete.CreatedAt!.Value.AddHours(24), afterComplete.ExpiresAt);
        Assert.True(Math.Abs((afterComplete.CreatedAt.Value - completedAt).TotalSeconds) < 5);
        Assert.Null(afterComplete.AccessTokenHash); // no link generated yet

        // --- 6. Generate the public link. ---
        var link = await _linkService.GenerateLinkAsync(user.Id, file.Id);
        var accessToken = link.AccessToken;

        var afterLink = await _dbContext.Files.SingleAsync(f => f.Id == file.Id);
        Assert.False(string.IsNullOrWhiteSpace(afterLink.AccessTokenHash));

        // --- 7. Public access (no JWT at all — this test never authenticates as this user
        //        for anything past step 1's direct entity creation). ---
        var access = await _linkService.GetByAccessTokenAsync(accessToken);
        Assert.Equal(file.Id, access.FileId);
        Assert.Equal("lifecycle-document.pdf", access.OriginalFileName);

        // --- 8. Public download: real presigned GET, real bytes back, real Download row. ---
        var downloadResponse = await _downloadService.DownloadAsync(accessToken, "203.0.113.50", "FullLifecycleTest/1.0");

        var getResponse = await _httpClient.GetAsync(downloadResponse.DownloadUrl);
        getResponse.EnsureSuccessStatusCode();
        var downloadedBytes = await getResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(content, downloadedBytes); // byte-for-byte integrity, not just a status code
        Assert.Equal("application/pdf", getResponse.Content.Headers.ContentType?.MediaType);

        // --- 9. Download record, queryable exactly as GET /api/files/{id}/downloads would see it. ---
        var download = await _dbContext.Downloads.SingleAsync(d => d.FileId == file.Id);
        Assert.Equal("203.0.113.50", download.IpAddress);
        Assert.Equal("FullLifecycleTest/1.0", download.UserAgent);

        // SignalR delivery for this same download is proven end-to-end (real HubConnection,
        // real NotificationHub) by FileSharing.ApiTests.Notifications.NotificationHubTests — not
        // duplicated here, since this project has no SignalR/HTTP host of its own (see the
        // class-level comment on FileDownloadIntegrationTests for why that split is deliberate).

        // --- 10. Regenerate the link — old token must stop resolving publicly. ---
        var secondLink = await _linkService.GenerateLinkAsync(user.Id, file.Id);
        Assert.NotEqual(accessToken, secondLink.AccessToken);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => _linkService.GetByAccessTokenAsync(accessToken));

        var newTokenAccess = await _linkService.GetByAccessTokenAsync(secondLink.AccessToken);
        Assert.Equal(file.Id, newTokenAccess.FileId);

        // --- 11. Force expiration (backdate ExpiresAt via the same technique already
        //         established by PublicFilesEndpointsTests/ExpiredFileCleanupJobIntegrationTests —
        //         never waiting 24h, never hacking the domain rule itself). ---
        _dbContext.Entry(file).Property("ExpiresAt").CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-1);
        await _dbContext.SaveChangesAsync();

        // Expiry is enforced immediately by ExpiresAt comparison, independent of Hangfire ever
        // having run — same invariant already covered by
        // PublicFilesEndpointsTests.GetPublicFile_WithExpiredFilesToken_ReturnsNotFound, restated
        // here as one more link in this same chain rather than a fresh assertion on its own.
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => _linkService.GetByAccessTokenAsync(secondLink.AccessToken));
        Assert.True(await _storageService.ObjectExistsAsync(storageKey)); // Hangfire hasn't run yet

        // --- 12. Hangfire cleanup job actually runs (direct Execute, per CLAUDE.md's own testing
        //         guidance: never assert on Hangfire's internal scheduling). ---
        var cleanupJob = new ExpiredFileCleanupJob(
            _dbContext,
            _storageService,
            Options.Create(new ExpirationCleanupOptions { BatchSize = 100 }),
            new AppMetrics(),
            NullLogger<ExpiredFileCleanupJob>.Instance);

        var cleanupResult = await cleanupJob.ExecuteAsync();
        Assert.True(cleanupResult.Expired >= 1);
        Assert.Equal(0, cleanupResult.Failed);

        // --- 13. S3 object gone, File.Status == Expired, public link permanently unavailable. ---
        Assert.False(await _storageService.ObjectExistsAsync(storageKey));

        var finalFile = await _dbContext.Files.SingleAsync(f => f.Id == file.Id);
        Assert.Equal(FileStatus.Expired, finalFile.Status);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => _linkService.GetByAccessTokenAsync(secondLink.AccessToken));
    }
}
