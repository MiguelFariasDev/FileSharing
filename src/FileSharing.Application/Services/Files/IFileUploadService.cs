using FileSharing.Application.DTOs.Files;

namespace FileSharing.Application.Services.Files;

public interface IFileUploadService
{
    Task<InitiateUploadResponse> InitiateUploadAsync(
        Guid userId,
        InitiateUploadRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Throws <see cref="Common.Exceptions.ResourceNotFoundException"/> when the file does not
    /// exist or belongs to a different user, or <see cref="Common.Exceptions.ConflictException"/>
    /// when it exists but its current state (or the uploaded object) does not allow completion.
    /// </summary>
    Task<CompleteUploadResponse> CompleteUploadAsync(
        Guid userId,
        Guid fileId,
        CancellationToken cancellationToken = default);
}
