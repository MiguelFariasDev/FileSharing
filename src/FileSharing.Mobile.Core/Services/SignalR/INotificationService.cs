using FileSharing.Application.DTOs.Notifications;

namespace FileSharing.Mobile.Core.Services.SignalR;

public interface INotificationService : IAsyncDisposable
{
    event Action<FileDownloadedNotification>? FileDownloaded;
    event Action? StateChanged;

    NotificationConnectionState State { get; }

    Task StartAsync();
    Task StopAsync();
}
