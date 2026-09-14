namespace FileSharing.Mobile.Tests;

/// <summary>
/// System.Progress&lt;T&gt; captures SynchronizationContext.Current at construction time; when
/// none exists (the normal case for a plain xunit test — there is no UI/ASP.NET Core sync
/// context installed), it falls back to ThreadPool.QueueUserWorkItem, so Report(...) delivers
/// its callback asynchronously on a different thread rather than inline. Any test asserting on
/// a side effect of a Progress&lt;T&gt; callback must poll for it rather than assume it has
/// already run the instant the awaited call that triggered it returns.
/// </summary>
internal static class TestWait
{
    public static async Task UntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(2));

        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Condition was not met within the timeout.");

            await Task.Delay(10);
        }
    }
}
