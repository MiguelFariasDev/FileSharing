namespace FileSharing.Application.DTOs.Notifications;

/// <summary>
/// Pushed to the file's owner over SignalR when a public download is authorized. Represents
/// "a download was registered/authorized for this file" — not confirmation that the downloader
/// finished transferring the bytes (see IFileDownloadNotifier remarks).
///
/// Deliberately minimal: no AccessToken, AccessTokenHash, presigned URL, StorageKey, downloader
/// IpAddress/UserAgent, or any other secret/PII beyond what the owner needs to know "your file
/// was downloaded". IP/UserAgent are recorded in the Download history (Etapa 5) but never leave
/// the server in this event.
/// </summary>
public record FileDownloadedNotification(Guid FileId, string OriginalFileName, DateTimeOffset DownloadedAt);
