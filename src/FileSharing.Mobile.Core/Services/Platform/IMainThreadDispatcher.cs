namespace FileSharing.Mobile.Core.Services.Platform;

/// <summary>Wraps Microsoft.Maui.ApplicationModel.MainThread — lets a ViewModel safely apply a
/// property update that originated on a background thread (a SignalR callback, a timer tick)
/// without referencing MAUI directly.</summary>
public interface IMainThreadDispatcher
{
    void BeginInvokeOnMainThread(Action action);
}
