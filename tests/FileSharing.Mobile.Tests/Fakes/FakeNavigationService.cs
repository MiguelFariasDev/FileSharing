using FileSharing.Mobile.Core.Services.Platform;

namespace FileSharing.Mobile.Tests.Fakes;

public class FakeNavigationService : INavigationService
{
    public List<string> Visited { get; } = [];
    public int GoBackCount { get; private set; }
    public string? RootRoute { get; private set; }

    public Task GoToAsync(string route)
    {
        Visited.Add(route);
        return Task.CompletedTask;
    }

    public Task GoToAsync(string route, IDictionary<string, object> parameters)
    {
        Visited.Add(route);
        return Task.CompletedTask;
    }

    public Task GoBackAsync()
    {
        GoBackCount++;
        return Task.CompletedTask;
    }

    public Task GoToRootAsync(string route)
    {
        RootRoute = route;
        Visited.Add(route);
        return Task.CompletedTask;
    }
}
