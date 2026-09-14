using FileSharing.Mobile.Core.Services.Platform;

namespace FileSharing.Mobile.Services.Platform;

public class ClipboardService : IClipboardService
{
    public Task SetTextAsync(string text) => Clipboard.Default.SetTextAsync(text);
}
