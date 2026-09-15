using FileSharing.Application.DTOs.Notifications;

namespace FileSharing.Web.Services.Notifications;

/// <summary>
/// Keeps a bounded, in-memory feed of "your file was downloaded" events for the Notifications
/// page (/notifications) — Scoped, same lifetime as AuthTokenProvider/SignalRNotificationService,
/// so it lives exactly as long as the authenticated circuit and never persists across sessions.
///
/// Deliberately session-only: there is no Api endpoint that returns a historical notification
/// feed (only per-file download history, GET /api/files/{id}/downloads), so this can only ever
/// reflect events actually received over SignalR since the circuit started — see
/// docs/navigation-flows.md "Known Issues" for why a full historical inbox is out of scope.
/// </summary>
public class NotificationInboxService
{
    private const int MaxEntries = 50;
    private readonly List<NotificationEntry> _entries = [];

    public IReadOnlyList<NotificationEntry> Entries => _entries;

    public event Action? Changed;

    public void Add(FileDownloadedNotification notification)
    {
        _entries.Insert(0, new NotificationEntry(notification, DateTimeOffset.UtcNow));

        if (_entries.Count > MaxEntries)
            _entries.RemoveAt(_entries.Count - 1);

        Changed?.Invoke();
    }
}

public record NotificationEntry(FileDownloadedNotification Notification, DateTimeOffset ReceivedAt);
