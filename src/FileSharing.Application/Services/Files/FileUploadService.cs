using System.Diagnostics;
using FileSharing.Application.Abstractions.Persistence;
using FileSharing.Application.Abstractions.Storage;
using FileSharing.Application.Common;
using FileSharing.Application.Common.Errors;
using FileSharing.Application.Common.Exceptions;
using FileSharing.Application.DTOs.Files;
using FileSharing.Application.Observability;
using FileSharing.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using File = FileSharing.Domain.Entities.File;

namespace FileSharing.Application.Services.Files;

public class FileUploadService : IFileUploadService
{
    private const string FileNotFoundError = "Arquivo não encontrado.";

    private readonly IApplicationDbContext _dbContext;
    private readonly IFileStorageService _fileStorageService;
    private readonly AppMetrics _metrics;
    private readonly ILogger<FileUploadService> _logger;

    public FileUploadService(
        IApplicationDbContext dbContext,
        IFileStorageService fileStorageService,
        AppMetrics metrics,
        ILogger<FileUploadService> logger)
    {
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<InitiateUploadResponse> InitiateUploadAsync(
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

        // Never the original file name (user-supplied, potentially sensitive text) or the
        // StorageKey/presigned URL — FileId is the stable identifier every other log about this
        // file will also use.
        _logger.LogInformation(
            "Upload initiated. FileId={FileId} UserId={UserId} ContentType={ContentType} SizeBytes={SizeBytes}",
            file.Id, userId, request.ContentType, request.SizeBytes);
        _metrics.UploadInitiated();

        return new InitiateUploadResponse(file.Id, presignedUrl.Url, presignedUrl.ExpiresAt);
    }

    public async Task<CompleteUploadResponse> CompleteUploadAsync(
        Guid userId,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var file = await _dbContext.Files.SingleOrDefaultAsync(f => f.Id == fileId, cancellationToken);

        // Same generic error for "does not exist" and "belongs to someone else" — never
        // reveal to a caller whether a given file id belongs to another user.
        if (file is null || file.UserId != userId)
        {
            _logger.LogWarning("Complete upload rejected: file not found or not owned by caller. FileId={FileId} UserId={UserId}", fileId, userId);
            _metrics.UploadCompleted(success: false, stopwatch.Elapsed.TotalMilliseconds);
            throw new ResourceNotFoundException(FileErrorCode.NotFound, FileNotFoundError);
        }

        if (!file.IsPendingUpload)
        {
            _logger.LogWarning("Complete upload rejected: not pending confirmation. FileId={FileId}", file.Id);
            _metrics.UploadCompleted(success: false, stopwatch.Elapsed.TotalMilliseconds);
            throw new ConflictException(FileErrorCode.InvalidUploadState, "Este upload não está pendente de confirmação.");
        }

        var metadata = await _fileStorageService.GetObjectMetadataAsync(file.StorageKey, cancellationToken);

        if (metadata is null)
        {
            _logger.LogWarning("Complete upload rejected: object not found in storage. FileId={FileId}", file.Id);
            _metrics.UploadCompleted(success: false, stopwatch.Elapsed.TotalMilliseconds);
            throw new ConflictException(FileErrorCode.InvalidUploadState, "O objeto não foi encontrado no armazenamento.");
        }

        if (metadata.SizeBytes != file.SizeBytes)
        {
            _logger.LogWarning("Complete upload rejected: uploaded size does not match the declared size. FileId={FileId}", file.Id);
            _metrics.UploadCompleted(success: false, stopwatch.Elapsed.TotalMilliseconds);
            throw new ConflictException(FileErrorCode.InvalidUploadState, "O tamanho do objeto enviado não corresponde ao declarado.");
        }

        if (!string.IsNullOrEmpty(metadata.ContentType) &&
            !metadata.ContentType.Equals(file.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Complete upload rejected: uploaded Content-Type does not match the declared value. FileId={FileId}", file.Id);
            _metrics.UploadCompleted(success: false, stopwatch.Elapsed.TotalMilliseconds);
            throw new ConflictException(FileErrorCode.InvalidUploadState, "O Content-Type do objeto enviado não corresponde ao declarado.");
        }

        file.CompleteUpload(DateTimeOffset.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Upload completed. FileId={FileId} UserId={UserId} SizeBytes={SizeBytes} ExpiresAt={ExpiresAt}",
            file.Id, userId, file.SizeBytes, file.ExpiresAt);
        _metrics.UploadCompleted(success: true, stopwatch.Elapsed.TotalMilliseconds);

        return new CompleteUploadResponse(
            file.Id,
            file.OriginalFileName,
            file.SizeBytes,
            file.CreatedAt!.Value,
            file.ExpiresAt!.Value);
    }
}
