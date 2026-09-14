using FileSharing.Web.Models;

namespace FileSharing.Web.Services;

/// <summary>
/// Simple pub/sub for non-blocking notifications — Components/Shared/ToastContainer.razor is
/// the single renderer (mounted once in MainLayout), so any component/service (including
/// Dashboard reacting to a real-time FileDownloaded event) can raise a toast without knowing
/// where or how it gets displayed. Never uses the browser's alert().
/// </summary>
public class ToastService
{
    public event Action<ToastMessage>? OnShow;

    public void Show(string text, ToastLevel level = ToastLevel.Info) =>
        OnShow?.Invoke(new ToastMessage(Guid.NewGuid(), text, level));

    public void ShowSuccess(string text) => Show(text, ToastLevel.Success);
    public void ShowError(string text) => Show(text, ToastLevel.Error);
    public void ShowWarning(string text) => Show(text, ToastLevel.Warning);
}
