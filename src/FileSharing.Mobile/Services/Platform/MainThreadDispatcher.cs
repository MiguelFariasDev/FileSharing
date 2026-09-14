using FileSharing.Mobile.Core.Services.Platform;

namespace FileSharing.Mobile.Services.Platform;

public class MainThreadDispatcher : IMainThreadDispatcher
{
    public void BeginInvokeOnMainThread(Action action) => MainThread.BeginInvokeOnMainThread(action);
}
