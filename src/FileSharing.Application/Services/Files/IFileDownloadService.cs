namespace FileSharing.Application.Services.Files;

public interface IFileDownloadService
{
    /// <summary>
    /// Public, unauthenticated download by plaintext access token. On success, registers a
    /// <c>Download</c> (the authorized attempt — see <see cref="DownloadFileOutcome"/> remarks
    /// in the implementation for why this, and not a completed transfer, is what "download" means
    /// here) and returns a short-lived presigned GET URL. Never differentiates "token never
    /// existed" from "file expired" from "object missing from storage" — always the same
    /// <see cref="DownloadFileOutcome.NotAvailable"/>.
    /// </summary>
    Task<DownloadFileOutcome> DownloadAsync(
        string accessToken,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default);
}
