using System.Net.Http.Headers;
using Amazon;
using Amazon.S3;
using FileSharing.Application.Abstractions.Storage;
using FileSharing.Application.Services.Files;
using FileSharing.Domain.Entities;
using FileSharing.Infrastructure.Persistence;
using FileSharing.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using CompressionType = FileSharing.Domain.Enums.CompressionType;
using File = FileSharing.Domain.Entities.File;

namespace FileSharing.IntegrationTests.Files;

/// <summary>
/// Exercises the Phase 5 public download flow (token -> File lookup -> storage check ->
/// Download record -> presigned GET URL) against real infrastructure — Postgres and LocalStack
/// S3 — instead of the InMemory DB + mocked storage used by FileSharing.ApiTests. Requires the
/// stack from infrastructure/docker/docker-compose.yml to be running:
///
///   cd infrastructure/docker
///   docker compose up -d
///   dotnet test tests/FileSharing.IntegrationTests
///
/// Deliberately reuses the exact same Application-layer services the Api wires up
/// (FilePublicLinkService, FileDownloadService) directly against a real ApplicationDbContext /
/// S3FileStorageService — the same "instantiate the real Infrastructure implementation, no
/// HTTP host" pattern already established by S3FileStorageServiceTests. This project has no
/// WebApplicationFactory/HTTP-pipeline infrastructure (that lives in FileSharing.ApiTests,
/// with an InMemory DB and mocked storage on purpose), so a full HTTP-level integration test
/// against real Postgres+LocalStack was intentionally not built here — that would be a second,
/// redundant test harness for a scenario the Application-level test below already covers
/// end-to-end (Postgres row in, LocalStack object in, real presigned URL out, Postgres row
/// verified after).
///
/// Never talks to real AWS: the endpoint, bucket and credentials below are all LocalStack-only
/// development values, matching src/FileSharing.Api/appsettings.Development.json.
/// </summary>
public class FileDownloadIntegrationTests : IAsyncLifetime
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
    private readonly string _storageKey = $"integration-tests/download-{Guid.NewGuid():N}";

    private Guid _userId;
    private Guid _fileId;

    public FileDownloadIntegrationTests()
    {
        // AWSSDK.S3 v4 only honors a custom endpoint via this environment variable —
        // AmazonS3Config.ServiceURL alone is not enough to redirect requests away from real
        // AWS (see StorageExtensions.cs for the same setup used by the Api).
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

        _linkService = new FilePublicLinkService(_dbContext);
        _downloadService = new FileDownloadService(_dbContext, _storageService);
    }

    public async Task InitializeAsync()
    {
        var user = new User($"integration-{Guid.NewGuid():N}@example.com", "dummy-hash");
        _userId = user.Id;

        var file = new File(user.Id, "document.pdf", _storageKey, "application/pdf", 1024, false, CompressionType.None);
        file.CompleteUpload(DateTimeOffset.UtcNow);
        _fileId = file.Id;

        _dbContext.Users.Add(user);
        _dbContext.Files.Add(file);
        await _dbContext.SaveChangesAsync();

        var content = "%PDF-1.4 fake content for download integration test"u8.ToArray();
        var presignedUpload = await _storageService.CreatePresignedUploadUrlAsync(_storageKey, "application/pdf");

        using var putContent = new ByteArrayContent(content);
        putContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var putResponse = await _httpClient.PutAsync(presignedUpload.Url, putContent);
        putResponse.EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync()
    {
        await _storageService.DeleteObjectAsync(_storageKey);

        var downloads = await _dbContext.Downloads.Where(d => d.FileId == _fileId).ToListAsync();
        _dbContext.Downloads.RemoveRange(downloads);

        var file = await _dbContext.Files.FindAsync(_fileId);
        if (file is not null)
            _dbContext.Files.Remove(file);

        var user = await _dbContext.Users.FindAsync(_userId);
        if (user is not null)
            _dbContext.Users.Remove(user);

        await _dbContext.SaveChangesAsync();

        _dbContext.Dispose();
        _s3Client.Dispose();
        _httpClient.Dispose();
    }

    [Fact]
    public async Task DownloadAsync_FullFlow_RegistersADownloadRow_AndReturnsAWorkingPresignedGetUrl()
    {
        // 1. Generate/associate a real token (Phase 4 service, real Postgres).
        var linkResult = await _linkService.GenerateLinkAsync(_userId, _fileId);
        Assert.True(linkResult.IsSuccess);
        var accessToken = linkResult.Value!.AccessToken;

        // 2. Call the public download flow (Phase 5 service, real Postgres + real LocalStack).
        var result = await _downloadService.DownloadAsync(accessToken, "203.0.113.10", "IntegrationTest/1.0");

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Value!.DownloadUrl));

        // Short-lived — configured 300s, well under the file's own 24h window.
        Assert.True(result.Value.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.True(result.Value.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(300).AddSeconds(5));

        // 3. The presigned URL must actually work (real GET against the real bucket).
        var getResponse = await _httpClient.GetAsync(result.Value.DownloadUrl);
        Assert.True(getResponse.IsSuccessStatusCode, await getResponse.Content.ReadAsStringAsync());

        // 4. Verify the Download row landed in Postgres.
        var download = await _dbContext.Downloads.SingleAsync(d => d.FileId == _fileId);
        Assert.Equal("203.0.113.10", download.IpAddress);
        Assert.Equal("IntegrationTest/1.0", download.UserAgent);
        Assert.Equal(TimeSpan.Zero, download.DownloadedAt.Offset);
    }

    // A test asserting that a bare, unsigned GET against the bucket is rejected was attempted
    // here and removed: LocalStack Community (the free image used by docker-compose.yml) does
    // not actually enforce S3 Block Public Access / bucket policies against anonymous requests
    // the way real AWS does (verified empirically — the unsigned request succeeded even with
    // Public Access Block configured by infrastructure/docker/localstack-init/01-create-bucket.sh).
    // That's a limitation of the LocalStack Community S3 emulation, not of this phase's code:
    // CreatePresignedDownloadUrlAsync (S3FileStorageService) never calls any ACL/policy-mutating
    // S3 API — it only calls the offline, local GetPreSignedURL computation, exactly like the
    // existing upload path — so there is nothing in Phase 5 that could grant public access in
    // the first place. See docs/security.md and the final report's "limitations" section.

    [Fact]
    public async Task DownloadAsync_WhenObjectIsMissingFromStorage_ReturnsNotAvailable_AndDoesNotRegisterADownload()
    {
        var missingKey = $"integration-tests/missing-{Guid.NewGuid():N}";
        var user = new User($"integration-{Guid.NewGuid():N}@example.com", "dummy-hash");
        var file = new File(user.Id, "ghost.pdf", missingKey, "application/pdf", 10, false, CompressionType.None);
        file.CompleteUpload(DateTimeOffset.UtcNow);

        _dbContext.Users.Add(user);
        _dbContext.Files.Add(file);
        await _dbContext.SaveChangesAsync();

        try
        {
            var linkResult = await _linkService.GenerateLinkAsync(user.Id, file.Id);
            Assert.True(linkResult.IsSuccess);

            // No object was ever PUT for this file's StorageKey.
            var result = await _downloadService.DownloadAsync(linkResult.Value!.AccessToken, "203.0.113.20", "IntegrationTest/1.0");

            Assert.False(result.IsSuccess);
            Assert.False(await _dbContext.Downloads.AnyAsync(d => d.FileId == file.Id));
        }
        finally
        {
            _dbContext.Files.Remove(file);
            _dbContext.Users.Remove(user);
            await _dbContext.SaveChangesAsync();
        }
    }
}
