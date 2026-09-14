namespace FileSharing.Application.DTOs.Files;

/// <summary>
/// One row of a file's download history (GET /api/files/{id}/downloads), owner-only.
/// Deliberately excludes IpAddress/UserAgent — those are recorded for audit purposes (Etapa 5)
/// but are not surfaced to the dashboard in this phase.
/// </summary>
public record DownloadHistoryEntryResponse(DateTimeOffset DownloadedAt);
