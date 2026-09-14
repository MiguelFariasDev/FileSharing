using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSharing.Application.DTOs.Files;
using FileSharing.Mobile.Core.Services.ApiClient;

namespace FileSharing.Mobile.Core.ViewModels;

/// <summary>
/// Backs the download-history screen (Fase 13 §22) — shows exactly what
/// GET /api/files/{id}/downloads returns and nothing more: DownloadedAt only. IpAddress/
/// UserAgent are deliberately never part of that DTO's contract (Fase 5/8 decision, unchanged
/// here) and are never fabricated or inferred client-side.
/// </summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly FileSharingApiClient _apiClient;

    public ObservableCollection<DownloadHistoryEntryResponse> Entries { get; } = [];

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? fileName;

    public bool IsEmpty => !IsLoading && !HasError && Entries.Count == 0;

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnHasErrorChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    private Guid _currentFileId;

    public HistoryViewModel(FileSharingApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task LoadAsync(Guid fileId)
    {
        _currentFileId = fileId;
        IsLoading = true;
        HasError = false;
        ErrorMessage = null;
        Entries.Clear();

        try
        {
            var result = await _apiClient.GetDownloadHistoryAsync(fileId);
            if (!result.IsSuccess)
            {
                HasError = true;
                ErrorMessage = result.Message ?? "Não foi possível carregar o histórico.";
                return;
            }

            foreach (var entry in result.Value!)
                Entries.Add(entry);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private Task Retry() => LoadAsync(_currentFileId);
}
