using Amazon;
using Amazon.S3;
using FileSharing.Application.Abstractions.Storage;
using FileSharing.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace FileSharing.IntegrationTests.Storage;

/// <summary>
/// Exercises S3FileStorageService against a real S3-compatible endpoint (LocalStack) instead
/// of mocks — these tests require the stack from infrastructure/docker/docker-compose.yml to
/// be running (see docs/database.md / docs/architecture.md for the exact commands):
///
///   cd infrastructure/docker
///   docker compose up -d
///   dotnet test tests/FileSharing.IntegrationTests
///
/// They never talk to real AWS: the endpoint, bucket and credentials below are all
/// LocalStack-only development values.
/// </summary>
public class S3FileStorageServiceTests : IAsyncLifetime
{
    private const string BucketName = "filesharing-dev";

    private readonly S3FileStorageService _sut;
    private readonly AmazonS3Client _s3Client;
    private readonly HttpClient _httpClient = new();
    private readonly string _storageKey = $"integration-tests/{Guid.NewGuid():N}";

    private const string LocalStackEndpoint = "http://localhost:4566";

    public S3FileStorageServiceTests()
    {
        // AWSSDK.S3 v4 only honors a custom endpoint via this environment variable —
        // AmazonS3Config.ServiceURL alone is not enough to redirect requests away from
        // real AWS (see StorageExtensions.cs for the same setup used by the API).
        Environment.SetEnvironmentVariable("AWS_ENDPOINT_URL_S3", LocalStackEndpoint);

        var s3Config = new AmazonS3Config
        {
            ServiceURL = LocalStackEndpoint,
            ForcePathStyle = true,
            RegionEndpoint = RegionEndpoint.USEast1
        };

        _s3Client = new AmazonS3Client("test", "test", s3Config);

        var options = Options.Create(new FileStorageOptions
        {
            BucketName = BucketName,
            Region = "us-east-1",
            PresignedUploadExpirationMinutes = 15
        });

        _sut = new S3FileStorageService(_s3Client, options);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _sut.DeleteObjectAsync(_storageKey);
        _s3Client.Dispose();
        _httpClient.Dispose();
    }

    [Fact]
    public async Task ObjectExistsAsync_ReturnsFalse_ForKeyThatWasNeverUploaded()
    {
        var exists = await _sut.ObjectExistsAsync($"never-uploaded/{Guid.NewGuid():N}");

        Assert.False(exists);
    }

    [Fact]
    public async Task PresignedUrl_AllowsDirectPutFromClient_AndObjectBecomesVisible()
    {
        const string contentType = "application/pdf";
        var content = "%PDF-1.4 fake content for integration test"u8.ToArray();

        var presigned = await _sut.CreatePresignedUploadUrlAsync(_storageKey, contentType);
        Assert.True(presigned.ExpiresAt > DateTimeOffset.UtcNow);

        using var putContent = new ByteArrayContent(content);
        putContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        var putResponse = await _httpClient.PutAsync(presigned.Url, putContent);
        Assert.True(putResponse.IsSuccessStatusCode, await putResponse.Content.ReadAsStringAsync());

        var exists = await _sut.ObjectExistsAsync(_storageKey);
        Assert.True(exists);

        var metadata = await _sut.GetObjectMetadataAsync(_storageKey);
        Assert.NotNull(metadata);
        Assert.Equal(content.Length, metadata!.SizeBytes);
        Assert.Equal(contentType, metadata.ContentType);
    }

    [Fact]
    public async Task DeleteObjectAsync_RemovesObject()
    {
        var key = $"integration-tests/delete-{Guid.NewGuid():N}";
        var presigned = await _sut.CreatePresignedUploadUrlAsync(key, "text/plain");

        using var content = new StringContent("delete me");
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        await _httpClient.PutAsync(presigned.Url, content);

        Assert.True(await _sut.ObjectExistsAsync(key));

        await _sut.DeleteObjectAsync(key);

        Assert.False(await _sut.ObjectExistsAsync(key));
    }
}
