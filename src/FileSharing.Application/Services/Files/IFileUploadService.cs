using FileSharing.Application.Common;
using FileSharing.Application.DTOs.Files;

namespace FileSharing.Application.Services.Files;

public interface IFileUploadService
{
    Task<Result<InitiateUploadResponse>> InitiateUploadAsync(
        Guid userId,
        InitiateUploadRequest request,
        CancellationToken cancellationToken = default);

    Task<CompleteUploadOutcome> CompleteUploadAsync(
        Guid userId,
        Guid fileId,
        CancellationToken cancellationToken = default);
}
