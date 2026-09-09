using Android.App;
using Android.Content;
using Microsoft.Maui.ApplicationModel;

namespace FileSharing.Mobile.Platforms.Android;

/// <summary>
/// Bridges MainActivity.OnActivityResult (a callback the OS invokes on the Activity) back
/// to an awaitable Task, so IFolderPickerService can await the Storage Access Framework's
/// directory picker like any other async API instead of dealing with Activity callbacks.
/// </summary>
internal static class ActivityResultBridge
{
    public const int OpenDocumentTreeRequestCode = 4231;

    private static TaskCompletionSource<global::Android.Net.Uri?>? _pendingRequest;

    public static Task<global::Android.Net.Uri?> RequestOpenDocumentTreeAsync()
    {
        _pendingRequest = new TaskCompletionSource<global::Android.Net.Uri?>();

        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);

        Platform.CurrentActivity!.StartActivityForResult(intent, OpenDocumentTreeRequestCode);

        return _pendingRequest.Task;
    }

    public static void Complete(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != OpenDocumentTreeRequestCode)
            return;

        var uri = resultCode == Result.Ok ? data?.Data : null;
        _pendingRequest?.TrySetResult(uri);
        _pendingRequest = null;
    }
}
