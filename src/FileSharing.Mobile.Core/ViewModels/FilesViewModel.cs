using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Platform;
using FileSharing.Mobile.Core.Services.SignalR;

namespace FileSharing.Mobile.Core.ViewModels;

/// <summary>
/// Backs the "Arquivos" tab — the full file list, split out of HomeViewModel (which now only
/// shows a summary) so each tab owns exactly one responsibility, mirroring the equivalent
/// Files/Dashboard split on FileSharing.Web. Registered as a singleton (one list, one SignalR
/// subscription for the whole app session) — same rationale HomeViewModel already documented.
/// </summary>
public partial class FilesViewModel : ObservableObject
{
    private readonly FileSharingApiClient _apiClient;
    private readonly INotificationService _notificationService;
    private readonly INavigationService _navigation;
    private readonly IMainThreadDispatcher _dispatcher;

    private bool _hasLoadedOnce;

    public ObservableCollection<FileItemViewModel> Files { get; } = [];

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isRefreshing;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private string? errorMessage;

    public bool IsEmpty => !IsLoading && !HasError && Files.Count == 0;

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnHasErrorChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    public FilesViewModel(
        FileSharingApiClient apiClient,
        INotificationService notificationService,
        INavigationService navigation,
        IMainThreadDispatcher dispatcher)
    {
        _apiClient = apiClient;
        _notificationService = notificationService;
        _navigation = navigation;
        _dispatcher = dispatcher;

        _notificationService.FileDownloaded += notification =>
            _dispatcher.BeginInvokeOnMainThread(() =>
                Files.FirstOrDefault(f => f.FileId == notification.FileId)?.IncrementDownloadCount());
    }

    public async Task EnsureLoadedAsync()
    {
        if (_hasLoadedOnce)
            return;

        await LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = null;

        try
        {
            var result = await _apiClient.GetMyFilesAsync();
            if (!result.IsSuccess)
            {
                HasError = true;
                ErrorMessage = result.Message ?? "Não foi possível carregar seus arquivos.";
                return;
            }

            _hasLoadedOnce = true;
            ReconcileFiles(result.Value!);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            await LoadAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private Task GoToUploadAsync() => _navigation.GoToAsync("//upload");

    /// <summary>Called on logout (ProfileViewModel.LogoutAsync) — a new session starts with an empty list.</summary>
    public void Reset()
    {
        Files.Clear();
        _hasLoadedOnce = false;
        HasError = false;
        ErrorMessage = null;
    }

    [RelayCommand]
    private Task OpenFileDetailsAsync(FileItemViewModel file) =>
        _navigation.GoToAsync($"filedetails?fileId={file.FileId}");

    private void ReconcileFiles(IReadOnlyList<Application.DTOs.Files.FileSummaryResponse> latest)
    {
        var latestById = latest.ToDictionary(f => f.FileId);

        for (var i = Files.Count - 1; i >= 0; i--)
        {
            if (!latestById.ContainsKey(Files[i].FileId))
                Files.RemoveAt(i);
        }

        foreach (var summary in latest)
        {
            var existing = Files.FirstOrDefault(f => f.FileId == summary.FileId);
            if (existing is not null)
                existing.UpdateFrom(summary);
            else
                Files.Add(new FileItemViewModel(summary));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }
}
