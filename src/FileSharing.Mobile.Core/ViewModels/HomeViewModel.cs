using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Core.Services.Platform;
using FileSharing.Mobile.Core.Services.SignalR;

namespace FileSharing.Mobile.Core.ViewModels;

/// <summary>
/// The app's dashboard (Fase 13 §11) — the authenticated user's file list, live-updated by
/// SignalR (download counts) and a local re-label tick (countdown text), without ever polling
/// the API on a timer. Registered as a singleton (see MauiProgram) — one instance for the whole
/// app session, matching how there is exactly one Home tab, so the SignalR subscription and the
/// re-label timer below are set up exactly once and never need to be torn down mid-session.
/// </summary>
public partial class HomeViewModel : ObservableObject
{
    private readonly FileSharingApiClient _apiClient;
    private readonly AuthSession _authSession;
    private readonly INotificationService _notificationService;
    private readonly INavigationService _navigation;
    private readonly IMainThreadDispatcher _dispatcher;

    private Timer? _relabelTimer;
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

    [ObservableProperty]
    private string? toastMessage;

    [ObservableProperty]
    private bool isToastVisible;

    public bool IsEmpty => !IsLoading && !HasError && Files.Count == 0;

    public string? UserEmail => _authSession.CurrentUser?.Email;

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnHasErrorChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    public HomeViewModel(
        FileSharingApiClient apiClient,
        AuthSession authSession,
        INotificationService notificationService,
        INavigationService navigation,
        IMainThreadDispatcher dispatcher)
    {
        _apiClient = apiClient;
        _authSession = authSession;
        _notificationService = notificationService;
        _navigation = navigation;
        _dispatcher = dispatcher;

        _notificationService.FileDownloaded += OnFileDownloaded;

        // 60s, not 30s like the Web dashboard — mobile favors battery life a bit more, and this
        // only ever recomputes already-fetched labels locally; it never calls the API.
        _relabelTimer = new Timer(_ => _dispatcher.BeginInvokeOnMainThread(RelabelFiles), null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
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
    private Task GoToUploadAsync() => _navigation.GoToAsync("upload");

    [RelayCommand]
    private Task OpenFileDetailsAsync(FileItemViewModel file) =>
        _navigation.GoToAsync($"filedetails?fileId={file.FileId}");

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _notificationService.StopAsync();
        _authSession.ClearSession();
        Files.Clear();
        _hasLoadedOnce = false;
        await _navigation.GoToRootAsync("//login");
    }

    /// <summary>Updates in place (never clears-then-repopulates) so a currently-open bottom
    /// sheet/details view bound to one of these FileItemViewModel instances keeps working
    /// through a background refresh instead of pointing at a now-discarded object.</summary>
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

    private void OnFileDownloaded(Application.DTOs.Notifications.FileDownloadedNotification notification)
    {
        _dispatcher.BeginInvokeOnMainThread(() =>
        {
            var file = Files.FirstOrDefault(f => f.FileId == notification.FileId);
            file?.IncrementDownloadCount();

            ToastMessage = $"Seu arquivo foi baixado: {notification.OriginalFileName}";
            IsToastVisible = true;
        });
    }

    private void RelabelFiles()
    {
        foreach (var file in Files)
            file.RefreshRemainingLabel();
    }

    [RelayCommand]
    private void DismissToast() => IsToastVisible = false;
}
