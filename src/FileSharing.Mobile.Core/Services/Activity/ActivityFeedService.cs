using System.Collections.ObjectModel;
using FileSharing.Application.DTOs.Notifications;
using FileSharing.Mobile.Core.Services.Platform;
using FileSharing.Mobile.Core.Services.SignalR;

namespace FileSharing.Mobile.Core.Services.Activity;

/// <summary>
/// Keeps a bounded, in-memory feed of "your file was downloaded" events for the Atividade tab —
/// registered as a singleton (like HomeViewModel/AuthSession) so it lives for the whole app
/// session and captures every SignalR FileDownloaded event regardless of which page is currently
/// visible, mirroring FileSharing.Web.Services.Notifications.NotificationInboxService.
///
/// Deliberately session-only: there is no Api endpoint that returns a historical notification
/// feed (only per-file download history, GET /api/files/{id}/downloads), so this can only ever
/// reflect events actually received over SignalR since the app was launched — see
/// docs/navigation-flows.md "Known Issues".
/// </summary>
public class ActivityFeedService
{
    private const int MaxEntries = 50;

    private readonly INotificationService _notificationService;
    private readonly IMainThreadDispatcher _dispatcher;

    public ObservableCollection<ActivityEntry> Entries { get; } = [];

    public ActivityFeedService(INotificationService notificationService, IMainThreadDispatcher dispatcher)
    {
        _notificationService = notificationService;
        _dispatcher = dispatcher;
        _notificationService.FileDownloaded += OnFileDownloaded;
    }

    private void OnFileDownloaded(FileDownloadedNotification notification)
    {
        _dispatcher.BeginInvokeOnMainThread(() =>
        {
            Entries.Insert(0, new ActivityEntry(notification, DateTimeOffset.UtcNow));

            if (Entries.Count > MaxEntries)
                Entries.RemoveAt(Entries.Count - 1);
        });
    }

    /// <summary>Cleared on logout (HomeViewModel.LogoutAsync) — a new session starts with an empty feed.</summary>
    public void Clear() => Entries.Clear();
}

public record ActivityEntry(FileDownloadedNotification Notification, DateTimeOffset ReceivedAt);
