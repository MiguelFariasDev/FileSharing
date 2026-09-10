using FileSharing.Application.Abstractions.Persistence;
using FileSharing.Application.Common;
using FileSharing.Application.DTOs.Files;
using Microsoft.EntityFrameworkCore;

namespace FileSharing.Application.Services.Files;

public class FilePublicLinkService : IFilePublicLinkService
{
    private const string FileNotFoundError = "Arquivo não encontrado.";
    private const string FileNotReadyError = "O upload ainda não foi concluído.";
    private const string FileExpiredError = "O arquivo expirou.";

    private readonly IApplicationDbContext _dbContext;

    public FilePublicLinkService(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<GenerateLinkOutcome> GenerateLinkAsync(
        Guid userId,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var file = await _dbContext.Files.SingleOrDefaultAsync(f => f.Id == fileId, cancellationToken);

        // Same generic error for "does not exist" and "belongs to someone else" — never
        // reveal to a caller whether a given file id belongs to another user.
        if (file is null || file.UserId != userId)
            return GenerateLinkOutcome.Failure(GenerateLinkFailureReason.NotFound, FileNotFoundError);

        if (!file.IsActive)
            return GenerateLinkOutcome.Failure(GenerateLinkFailureReason.Conflict, FileNotReadyError);

        var now = DateTimeOffset.UtcNow;
        if (file.IsExpired(now))
            return GenerateLinkOutcome.Failure(GenerateLinkFailureReason.Conflict, FileExpiredError);

        // A fresh token every call — regenerating invalidates whatever link was issued before,
        // since only the hash is kept and the old plaintext can never be recovered anyway.
        var accessToken = RandomTokenGenerator.Generate();
        file.AssignAccessToken(AccessTokenHasher.Hash(accessToken), now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return GenerateLinkOutcome.Success(new GenerateLinkResponse(file.Id, accessToken));
    }

    public async Task<PublicFileAccessOutcome> GetByAccessTokenAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var accessTokenHash = AccessTokenHasher.Hash(accessToken);

        var file = await _dbContext.Files
            .SingleOrDefaultAsync(f => f.AccessTokenHash == accessTokenHash, cancellationToken);

        if (file is null || !file.IsActive || file.IsExpired())
            return PublicFileAccessOutcome.NotAvailable();

        return PublicFileAccessOutcome.Success(new PublicFileAccessResponse(
            file.Id,
            file.OriginalFileName,
            file.SizeBytes,
            file.ContentType,
            file.ExpiresAt!.Value));
    }
}
