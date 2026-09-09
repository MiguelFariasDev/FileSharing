namespace FileSharing.Application.DTOs.Files;

/// <summary>
/// CompressionType is intentionally not part of the request: for this version of the
/// platform it is fully determined by <see cref="IsFolder"/> (folder uploads are always a
/// zip, single-file uploads are never recompressed), so it is derived server-side instead
/// of trusted from the client.
/// </summary>
public record InitiateUploadRequest(string FileName, string ContentType, long SizeBytes, bool IsFolder);
