namespace FileSharing.Mobile.Core.Services.Platform;

/// <summary>
/// Thin abstraction over Shell navigation (Shell.Current.GoToAsync) — keeps ViewModels testable
/// without a real MAUI Shell/AppShell instance. Route strings match AppShell.xaml's registered
/// routes exactly (see FileSharing.Mobile.AppShell).
/// </summary>
public interface INavigationService
{
    Task GoToAsync(string route);
    Task GoToAsync(string route, IDictionary<string, object> parameters);
    Task GoBackAsync();

    /// <summary>Replaces the entire navigation stack — used for the post-login/post-logout
    /// transition, where the user must never be able to "back" into the previous state.</summary>
    Task GoToRootAsync(string route);
}
