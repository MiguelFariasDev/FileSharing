using FileSharing.Mobile.Core.Services.Platform;

namespace FileSharing.Mobile.Services.Platform;

public class ShareService : IShareService
{
    public Task ShareTextAsync(string title, string text) =>
        Share.Default.RequestAsync(new ShareTextRequest(text) { Title = title });
}
