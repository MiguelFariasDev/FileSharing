using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSharing.Mobile.Core.Services.Activity;
using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Core.Services.Platform;

namespace FileSharing.Mobile.Core.ViewModels;

/// <summary>
/// Backs the Home tab — a lightweight summary (greeting, quick stats, a handful of recent
/// files, the latest activity line) rather than the full list, which now lives on its own
/// "Arquivos" tab (FilesViewModel) — mirrors the equivalent Dashboard/Files split on
/// FileSharing.Web. Registered as a singleton, same rationale as before (one Home tab for the
/// whole app session).
/// </summary>
public partial class HomeViewModel : ObservableObject
{
    private readonly FilesViewModel _filesViewModel;
    private readonly ActivityFeedService _activityFeed;
    private readonly AuthSession _authSession;
    private readonly INavigationService _navigation;

    public string? UserEmail => _authSession.CurrentUser?.Email;

    public ObservableCollection<FileItemViewModel> Files => _filesViewModel.Files;

    public bool IsLoading => _filesViewModel.IsLoading;
    public bool HasError => _filesViewModel.HasError;
    public string? ErrorMessage => _filesViewModel.ErrorMessage;
    public bool IsEmpty => _filesViewModel.IsEmpty;

    public int ActiveCount => Files.Count(f => f.IsActive);
    public int ExpiringSoonCount => Files.Count(f => f.IsActive && f.Summary.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow.AddHours(2));
    public int RecentDownloadsCount => _activityFeed.Entries.Count;

    public IEnumerable<FileItemViewModel> RecentFiles => Files.Take(3);

    public string? LastActivityLabel => _activityFeed.Entries.Count == 0
        ? null
        : $"{_activityFeed.Entries[0].Notification.OriginalFileName} foi baixado";

    public HomeViewModel(
        FilesViewModel filesViewModel,
        ActivityFeedService activityFeed,
        AuthSession authSession,
        INavigationService navigation)
    {
        _filesViewModel = filesViewModel;
        _activityFeed = activityFeed;
        _authSession = authSession;
        _navigation = navigation;

        _filesViewModel.PropertyChanged += (_, _) => RaiseAllComputedChanged();
        _filesViewModel.Files.CollectionChanged += (_, _) => RaiseAllComputedChanged();
        _activityFeed.Entries.CollectionChanged += (_, _) => RaiseAllComputedChanged();
    }

    private void RaiseAllComputedChanged()
    {
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(ExpiringSoonCount));
        OnPropertyChanged(nameof(RecentDownloadsCount));
        OnPropertyChanged(nameof(RecentFiles));
        OnPropertyChanged(nameof(LastActivityLabel));
    }

    public Task EnsureLoadedAsync() => _filesViewModel.EnsureLoadedAsync();

    [RelayCommand]
    private Task LoadAsync() => _filesViewModel.LoadCommand.ExecuteAsync(null);

    [RelayCommand]
    private Task GoToUploadAsync() => _navigation.GoToAsync("//upload");

    [RelayCommand]
    private Task GoToFilesAsync() => _navigation.GoToAsync("//files");

    [RelayCommand]
    private Task GoToActivityAsync() => _navigation.GoToAsync("//activity");

    [RelayCommand]
    private Task GoToProfileAsync() => _navigation.GoToAsync("profile");

    [RelayCommand]
    private Task OpenFileDetailsAsync(FileItemViewModel file) =>
        _navigation.GoToAsync($"filedetails?fileId={file.FileId}");
}
