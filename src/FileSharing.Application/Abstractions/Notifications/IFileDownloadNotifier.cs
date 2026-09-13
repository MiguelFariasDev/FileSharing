using FileSharing.Application.DTOs.Notifications;

namespace FileSharing.Application.Abstractions.Notifications;

/// <summary>
/// Abstracts real-time delivery of download notifications away from the rest of the
/// application — the current implementation uses SignalR (FileSharing.Api.Hubs), but no
/// SignalR/ASP.NET Core type is referenced outside FileSharing.Api.
///
/// Best-effort by design: a failure to deliver a notification must never affect whether a
/// download succeeds. <c>FileDownloadService</c> calls this only after the corresponding
/// <c>Download</c> has already been persisted and the presigned URL already issued, and
/// additionally wraps the call in its own try/catch as a second safety net — so
/// implementations are free to log/swallow their own failures without any special contract
/// obligation not to throw.
/// </summary>
public interface IFileDownloadNotifier
{
    Task NotifyDownloadAsync(
        Guid ownerUserId,
        FileDownloadedNotification notification,
        CancellationToken cancellationToken = default);
}
