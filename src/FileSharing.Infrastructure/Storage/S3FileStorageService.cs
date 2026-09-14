using Amazon.S3;
using Amazon.S3.Model;
using FileSharing.Application.Abstractions.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FileSharing.Infrastructure.Storage;

/// <summary>
/// Logging here is deliberately Debug-level and never includes a StorageKey — the higher-level
/// Application services (FileUploadService, FileDownloadService, ExpiredFileCleanupJob) already
/// log each operation's outcome at Information/Warning with the meaningful identifier (FileId),
/// so this layer's own logs exist purely as low-level, dev-time detail for diagnosing this
/// specific S3 call — never a duplicate of what the caller already recorded.
/// </summary>
public class S3FileStorageService : IFileStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly FileStorageOptions _options;
    private readonly ILogger<S3FileStorageService> _logger;

    public S3FileStorageService(IAmazonS3 s3Client, IOptions<FileStorageOptions> options, ILogger<S3FileStorageService> logger)
    {
        _s3Client = s3Client;
        _options = options.Value;
        _logger = logger;
    }

    public Task<PresignedUploadUrl> CreatePresignedUploadUrlAsync(
        string storageKey,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_options.PresignedUploadExpirationMinutes);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = storageKey,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = expiresAt
        };

        // The SDK defaults presigned URLs to https regardless of the configured service
        // endpoint's scheme. Honor an explicit http endpoint (e.g. a local S3-compatible
        // service without TLS) instead of forcing a scheme mismatch on the caller.
        if (Uri.TryCreate(_s3Client.Config.ServiceURL, UriKind.Absolute, out var serviceUri) &&
            serviceUri.Scheme == Uri.UriSchemeHttp)
        {
            request.Protocol = Protocol.HTTP;
        }

        // GetPreSignedURL is a local HMAC computation — no network call is made, so no
        // cancellation point exists to honor cancellationToken here.
        var url = _s3Client.GetPreSignedURL(request);

        // Never the URL itself (it carries the StorageKey plus AWS signature parameters).
        _logger.LogDebug("Presigned upload URL created. ExpiresAt={ExpiresAt}", expiresAt);

        return Task.FromResult(new PresignedUploadUrl(url, new DateTimeOffset(expiresAt, TimeSpan.Zero)));
    }

    public Task<PresignedDownloadUrl> CreatePresignedDownloadUrlAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        var expiresAt = DateTime.UtcNow.AddSeconds(_options.DownloadUrlExpirationSeconds);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = storageKey,
            Verb = HttpVerb.GET,
            Expires = expiresAt
        };

        if (Uri.TryCreate(_s3Client.Config.ServiceURL, UriKind.Absolute, out var serviceUri) &&
            serviceUri.Scheme == Uri.UriSchemeHttp)
        {
            request.Protocol = Protocol.HTTP;
        }

        // GetPreSignedURL is a local HMAC computation — no network call is made, so no
        // cancellation point exists to honor cancellationToken here.
        var url = _s3Client.GetPreSignedURL(request);

        // Never the URL itself (it carries the StorageKey plus AWS signature parameters).
        _logger.LogDebug("Presigned download URL created. ExpiresAt={ExpiresAt}", expiresAt);

        return Task.FromResult(new PresignedDownloadUrl(url, new DateTimeOffset(expiresAt, TimeSpan.Zero)));
    }

    public async Task<bool> ObjectExistsAsync(string storageKey, CancellationToken cancellationToken = default) =>
        await GetObjectMetadataAsync(storageKey, cancellationToken) is not null;

    public async Task<StorageObjectMetadata?> GetObjectMetadataAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _s3Client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest { BucketName = _options.BucketName, Key = storageKey },
                cancellationToken);

            _logger.LogDebug("Object metadata retrieved. Found=true");
            return new StorageObjectMetadata(response.ContentLength, response.Headers.ContentType);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug("Object metadata retrieved. Found=false");
            return null;
        }
    }

    public async Task DeleteObjectAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        await _s3Client.DeleteObjectAsync(
            new DeleteObjectRequest { BucketName = _options.BucketName, Key = storageKey },
            cancellationToken);

        _logger.LogDebug("Storage object delete requested.");
    }
}
