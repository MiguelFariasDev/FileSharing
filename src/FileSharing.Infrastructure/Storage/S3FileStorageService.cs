using Amazon.S3;
using Amazon.S3.Model;
using FileSharing.Application.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;
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
    /// <summary>DI key for a second IAmazonS3 registration used only to sign presigned URLs — see StorageExtensions/PresignKeyedServiceName.</summary>
    public const string PresignClientKey = "s3-presign";

    private readonly IAmazonS3 _s3Client;

    // A presigned URL's signature covers the Host it was signed for — rewriting the URL's host
    // after signing would invalidate it. When the API talks to S3 over one hostname (a Docker
    // Compose service name/internal DNS, reachable only from other containers) but the
    // presigned URL must be usable from outside that network (a browser on the host, a Mobile
    // emulator/device), the fix is to *sign* with a client configured for the externally-reachable
    // endpoint in the first place — GetPreSignedURL is a local HMAC computation, so this never
    // makes a network call against that endpoint, it only changes what host ends up in the URL
    // and in the signature. In production (no PublicServiceURL configured) this is the exact
    // same client as _s3Client, so there is no behavioral difference at all.
    private readonly IAmazonS3 _presignClient;

    private readonly FileStorageOptions _options;
    private readonly ILogger<S3FileStorageService> _logger;

    public S3FileStorageService(
        IAmazonS3 s3Client,
        [FromKeyedServices(PresignClientKey)] IAmazonS3 presignClient,
        IOptions<FileStorageOptions> options,
        ILogger<S3FileStorageService> logger)
    {
        _s3Client = s3Client;
        _presignClient = presignClient;
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
        if (Uri.TryCreate(_presignClient.Config.ServiceURL, UriKind.Absolute, out var serviceUri) &&
            serviceUri.Scheme == Uri.UriSchemeHttp)
        {
            request.Protocol = Protocol.HTTP;
        }

        // GetPreSignedURL is a local HMAC computation — no network call is made, so no
        // cancellation point exists to honor cancellationToken here. Signed with _presignClient,
        // not _s3Client — see the field's remarks for why (Docker-internal vs externally-reachable
        // endpoint).
        var url = _presignClient.GetPreSignedURL(request);

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

        if (Uri.TryCreate(_presignClient.Config.ServiceURL, UriKind.Absolute, out var serviceUri) &&
            serviceUri.Scheme == Uri.UriSchemeHttp)
        {
            request.Protocol = Protocol.HTTP;
        }

        // GetPreSignedURL is a local HMAC computation — no network call is made, so no
        // cancellation point exists to honor cancellationToken here. Signed with _presignClient,
        // not _s3Client — see the field's remarks for why (Docker-internal vs externally-reachable
        // endpoint).
        var url = _presignClient.GetPreSignedURL(request);

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
