using FileSharing.Mobile.Core.Services.Platform;

namespace FileSharing.Mobile.Tests.Fakes;

public class FakeClipboardService : IClipboardService
{
    public string? CopiedText { get; private set; }

    public Task SetTextAsync(string text)
    {
        CopiedText = text;
        return Task.CompletedTask;
    }
}

public class FakeShareService : IShareService
{
    public string? SharedText { get; private set; }

    public Task ShareTextAsync(string title, string text)
    {
        SharedText = text;
        return Task.CompletedTask;
    }
}

/// <summary>Runs the action synchronously and immediately — a unit test has no real UI thread
/// to marshal onto, and does not need one.</summary>
public class ImmediateMainThreadDispatcher : IMainThreadDispatcher
{
    public void BeginInvokeOnMainThread(Action action) => action();
}
