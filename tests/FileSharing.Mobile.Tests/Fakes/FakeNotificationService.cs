using FileSharing.Application.DTOs.Notifications;
using FileSharing.Mobile.Core.Services.SignalR;

namespace FileSharing.Mobile.Tests.Fakes;

public class FakeNotificationService : INotificationService
{
    public event Action<FileDownloadedNotification>? FileDownloaded;
    public event Action? StateChanged;

    public NotificationConnectionState State { get; private set; } = NotificationConnectionState.Disconnected;
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }

    public Task StartAsync()
    {
        StartCount++;
        State = NotificationConnectionState.Connected;
        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        StopCount++;
        State = NotificationConnectionState.Disconnected;
        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public void RaiseFileDownloaded(FileDownloadedNotification notification) => FileDownloaded?.Invoke(notification);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
