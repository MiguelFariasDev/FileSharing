using FileSharing.Application.Abstractions.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FileSharing.Api.HealthChecks;

/// <summary>
/// Readiness check for S3/LocalStack. Deliberately goes through <see cref="IFileStorageService"/>
/// (the same abstraction FileUploadService/FileDownloadService already use) instead of the raw
/// <c>IAmazonS3</c> client — an S3-specific existence check on a key nobody could ever have
/// uploaded is a safe, read-only, side-effect-free probe (never PutObject/DeleteObject, never a
/// real file), and reusing the existing abstraction means this check automatically respects
/// whatever endpoint (LocalStack in Development, real S3 in production) StorageExtensions
/// already wired up — no separate AWS client/credentials wiring of its own.
///
/// Also means FileSharing.ApiTests exercises this exactly like every other test there: against
/// its already-mocked <c>IFileStorageService</c>, with no real network call and no risk of this
/// health check accidentally reaching real AWS from a test run.
/// </summary>
public class S3HealthCheck : IHealthCheck
{
    // Not a real object — GetObjectMetadataAsync returning null (not found) for this key is
    // just as much a "storage is reachable" signal as finding something would be.
    private const string ProbeKey = "__healthcheck__/probe";

    private readonly IFileStorageService _fileStorageService;

    public S3HealthCheck(IFileStorageService fileStorageService)
    {
        _fileStorageService = fileStorageService;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _fileStorageService.GetObjectMetadataAsync(ProbeKey, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Object storage is not reachable.", ex);
        }
    }
}
