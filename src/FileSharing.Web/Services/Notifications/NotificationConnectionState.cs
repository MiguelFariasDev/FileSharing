namespace FileSharing.Web.Services.Notifications;

/// <summary>
/// Simplified view of HubConnectionState for the UI — collapses SignalR's own enum into just
/// what a small, non-dominant status indicator needs to show (see Components/Shared/ConnectionStatus.razor).
/// </summary>
public enum NotificationConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting
}
