using FileSharing.Mobile.Core.Services.Platform;

namespace FileSharing.Mobile.Services.Platform;

public class NavigationService : INavigationService
{
    public Task GoToAsync(string route) => Shell.Current.GoToAsync(route);

    public Task GoToAsync(string route, IDictionary<string, object> parameters) => Shell.Current.GoToAsync(route, parameters);

    public Task GoBackAsync() => Shell.Current.GoToAsync("..");

    public Task GoToRootAsync(string route) => Shell.Current.GoToAsync(route);
}
