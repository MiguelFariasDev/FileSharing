using FileSharing.Application.DTOs.Files;

namespace FileSharing.Application.Services.Files;

public interface IFilePublicLinkService
{
    /// <summary>
    /// Generates a brand-new public access token for a file the caller owns. Calling this
    /// again for the same file issues a new token and replaces the previous one — there is
    /// no way to recover a token already returned once, since only its hash is stored.
    /// Throws <see cref="Common.Exceptions.ResourceNotFoundException"/> when the file does not
    /// exist or belongs to a different user, or <see cref="Common.Exceptions.ConflictException"/>
    /// when it exists but isn't ready (still pending) or has already expired.
    /// </summary>
    Task<GenerateLinkResponse> GenerateLinkAsync(Guid userId, Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Public, unauthenticated lookup by plaintext access token. Never differentiates
    /// "token never existed" from "file expired" from "file was removed" — all three throw the
    /// exact same <see cref="Common.Exceptions.ResourceNotFoundException"/> (same code, same
    /// generic message).
    /// </summary>
    Task<PublicFileAccessResponse> GetByAccessTokenAsync(string accessToken, CancellationToken cancellationToken = default);
}
