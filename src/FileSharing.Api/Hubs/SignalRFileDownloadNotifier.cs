using System.Diagnostics;
using FileSharing.Application.Abstractions.Notifications;
using FileSharing.Application.DTOs.Notifications;
using FileSharing.Application.Observability;
using Microsoft.AspNetCore.SignalR;

namespace FileSharing.Api.Hubs;

/// <summary>
/// SignalR implementation of <see cref="IFileDownloadNotifier"/> — the only place in the
/// codebase where FileSharing.Application's notification abstraction meets an actual SignalR
/// type. Lives in FileSharing.Api (not Infrastructure) because it depends on
/// <see cref="NotificationHub"/>, which itself must live in Api to be mapped by
/// Program.cs — matching the placement already established for background-job/infrastructure
/// concerns that need a specific layer's types (see CLAUDE.md's "Api ... SignalR hub
/// registration" bullet).
///
/// Addresses the owner via <c>Clients.User(ownerUserId)</c> — never <c>Clients.All</c> or a
/// client-supplied group — so this delivers to every one of that user's active connections
/// (multiple tabs/devices) and never to anyone else, relying entirely on
/// <see cref="SubClaimUserIdProvider"/> to have derived each connection's identifier from its
/// own JWT.
/// </summary>
public class SignalRFileDownloadNotifier : IFileDownloadNotifier
{
    public const string FileDownloadedEvent = "FileDownloaded";

    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly AppMetrics _metrics;
    private readonly ILogger<SignalRFileDownloadNotifier> _logger;

    public SignalRFileDownloadNotifier(IHubContext<NotificationHub> hubContext, AppMetrics metrics, ILogger<SignalRFileDownloadNotifier> logger)
    {
        _hubContext = hubContext;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task NotifyDownloadAsync(
        Guid ownerUserId,
        FileDownloadedNotification notification,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _hubContext.Clients.User(ownerUserId.ToString())
                .SendAsync(FileDownloadedEvent, notification, cancellationToken);

            _logger.LogInformation(
                "Download notification sent. FileId={FileId} UserId={UserId} DurationMs={DurationMs}",
                notification.FileId, ownerUserId, stopwatch.Elapsed.TotalMilliseconds);
            _metrics.SignalRNotification(success: true);
        }
        catch (Exception ex)
        {
            // Best-effort: never let a SignalR/transport failure propagate out of here (the
            // download itself has already succeeded by the time this is called — see
            // FileDownloadService). Logged with FileId/UserId only for correlation — never a
            // token, hash, connection id, or presigned URL, none of which this type ever sees
            // in the first place.
            _logger.LogWarning(
                ex,
                "Failed to send download notification. FileId={FileId} UserId={UserId} DurationMs={DurationMs}",
                notification.FileId, ownerUserId, stopwatch.Elapsed.TotalMilliseconds);
            _metrics.SignalRNotification(success: false);
        }
    }
}
