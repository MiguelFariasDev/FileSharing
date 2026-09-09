using FileSharing.Application.Abstractions.Persistence;
using FileSharing.Application.Abstractions.Storage;
using FileSharing.Application.Common;
using FileSharing.Application.DTOs.Files;
using FileSharing.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using File = FileSharing.Domain.Entities.File;

namespace FileSharing.Application.Services.Files;

public class FileUploadService : IFileUploadService
{
    private const string FileNotFoundError = "Arquivo não encontrado.";

    private readonly IApplicationDbContext _dbContext;
    private readonly IFileStorageService _fileStorageService;

    public FileUploadService(IApplicationDbContext dbContext, IFileStorageService fileStorageService)
    {
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
    }

    public async Task<Result<InitiateUploadResponse>> InitiateUploadAsync(
        Guid userId,
        InitiateUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        var compressionType = request.IsFolder ? CompressionType.Zip : CompressionType.None;
        var storageKey = RandomTokenGenerator.Generate();

        var file = new File(
            userId,
            request.FileName,
            storageKey,
            request.ContentType,
            request.SizeBytes,
            request.IsFolder,
            compressionType);

        _dbContext.Files.Add(file);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var presignedUrl = await _fileStorageService.CreatePresignedUploadUrlAsync(
            storageKey,
            request.ContentType,
            cancellationToken);

        return Result<InitiateUploadResponse>.Success(
            new InitiateUploadResponse(file.Id, presignedUrl.Url, presignedUrl.ExpiresAt));
    }

    public async Task<CompleteUploadOutcome> CompleteUploadAsync(
        Guid userId,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var file = await _dbContext.Files.SingleOrDefaultAsync(f => f.Id == fileId, cancellationToken);

        // Same generic error for "does not exist" and "belongs to someone else" — never
        // reveal to a caller whether a given file id belongs to another user.
        if (file is null || file.UserId != userId)
            return CompleteUploadOutcome.Failure(CompleteUploadFailureReason.NotFound, FileNotFoundError);

        if (!file.IsPendingUpload)
            return CompleteUploadOutcome.Failure(CompleteUploadFailureReason.Conflict, "Este upload não está pendente de confirmação.");

        var metadata = await _fileStorageService.GetObjectMetadataAsync(file.StorageKey, cancellationToken);

        if (metadata is null)
            return CompleteUploadOutcome.Failure(CompleteUploadFailureReason.Conflict, "O objeto não foi encontrado no armazenamento.");

        if (metadata.SizeBytes != file.SizeBytes)
            return CompleteUploadOutcome.Failure(CompleteUploadFailureReason.Conflict, "O tamanho do objeto enviado não corresponde ao declarado.");

        if (!string.IsNullOrEmpty(metadata.ContentType) &&
            !metadata.ContentType.Equals(file.ContentType, StringComparison.OrdinalIgnoreCase))
            return CompleteUploadOutcome.Failure(CompleteUploadFailureReason.Conflict, "O Content-Type do objeto enviado não corresponde ao declarado.");

        file.CompleteUpload(DateTimeOffset.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CompleteUploadOutcome.Success(new CompleteUploadResponse(
            file.Id,
            file.OriginalFileName,
            file.SizeBytes,
            file.CreatedAt!.Value,
            file.ExpiresAt!.Value));
    }
}
