namespace FileSharing.Application.Services.Files;

public interface IFilePublicLinkService
{
    /// <summary>
    /// Generates a brand-new public access token for a file the caller owns. Calling this
    /// again for the same file issues a new token and replaces the previous one — there is
    /// no way to recover a token already returned once, since only its hash is stored.
    /// </summary>
    Task<GenerateLinkOutcome> GenerateLinkAsync(Guid userId, Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Public, unauthenticated lookup by plaintext access token. Never differentiates
    /// "token never existed" from "file expired" from "file was removed" — see
    /// <see cref="PublicFileAccessOutcome"/>.
    /// </summary>
    Task<PublicFileAccessOutcome> GetByAccessTokenAsync(string accessToken, CancellationToken cancellationToken = default);
}
