using FileSharing.Application.DTOs.Files;

namespace FileSharing.Application.Services.Files;

public interface IFileDownloadService
{
    /// <summary>
    /// Public, unauthenticated download by plaintext access token. On success, registers a
    /// <c>Download</c> (the authorized attempt — see the implementation's remarks for why this,
    /// and not a completed transfer, is what "download" means here) and returns a short-lived
    /// presigned GET URL. Never differentiates "token never existed" from "file expired" from
    /// "object missing from storage" — all three throw the exact same
    /// <see cref="Common.Exceptions.ResourceNotFoundException"/> (same code, same generic message).
    /// </summary>
    Task<DownloadUrlResponse> DownloadAsync(
        string accessToken,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default);
}
