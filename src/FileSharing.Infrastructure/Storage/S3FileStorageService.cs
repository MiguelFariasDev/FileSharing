using Amazon.S3;
using Amazon.S3.Model;
using FileSharing.Application.Abstractions.Storage;
using Microsoft.Extensions.Options;

namespace FileSharing.Infrastructure.Storage;

public class S3FileStorageService : IFileStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly FileStorageOptions _options;

    public S3FileStorageService(IAmazonS3 s3Client, IOptions<FileStorageOptions> options)
    {
        _s3Client = s3Client;
        _options = options.Value;
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

        return Task.FromResult(new PresignedUploadUrl(url, new DateTimeOffset(expiresAt, TimeSpan.Zero)));
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

            return new StorageObjectMetadata(response.ContentLength, response.Headers.ContentType);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteObjectAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        await _s3Client.DeleteObjectAsync(
            new DeleteObjectRequest { BucketName = _options.BucketName, Key = storageKey },
            cancellationToken);
    }
}
