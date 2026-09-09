using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSharing.Mobile.Models;
using FileSharing.Mobile.Services.Api;
using FileSharing.Mobile.Services.Upload;

namespace FileSharing.Mobile.ViewModels;

public partial class UploadViewModel : ObservableObject
{
    private readonly FileSharingApiClient _apiClient;
    private readonly IFilePickerService _filePickerService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IFileUploadService _fileUploadService;

    private CancellationTokenSource? _uploadCancellation;

    [ObservableProperty]
    private UploadableItem? selectedItem;

    [ObservableProperty]
    private string statusText = "Faça login para começar.";

    [ObservableProperty]
    private double uploadProgress;

    [ObservableProperty]
    private bool isUploading;

    [ObservableProperty]
    private string email = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private bool isAuthenticated;

    public bool IsNotAuthenticated => !IsAuthenticated;
    public bool IsNotUploading => !IsUploading;

    partial void OnIsAuthenticatedChanged(bool value) => OnPropertyChanged(nameof(IsNotAuthenticated));
    partial void OnIsUploadingChanged(bool value) => OnPropertyChanged(nameof(IsNotUploading));

    public UploadViewModel(
        FileSharingApiClient apiClient,
        IFilePickerService filePickerService,
        IFolderPickerService folderPickerService,
        IFileUploadService fileUploadService)
    {
        _apiClient = apiClient;
        _filePickerService = filePickerService;
        _folderPickerService = folderPickerService;
        _fileUploadService = fileUploadService;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        try
        {
            StatusText = "Entrando...";
            await _apiClient.LoginAsync(Email, Password);
            IsAuthenticated = true;
            StatusText = "Autenticado. Selecione um arquivo ou pasta.";
        }
        catch (Exception ex)
        {
            StatusText = $"Falha no login: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task PickFileAsync()
    {
        try
        {
            var item = await _filePickerService.PickFileAsync();
            if (item is null)
                return;

            SelectedItem = item;
            StatusText = $"Selecionado: {item.FileName}";
        }
        catch (Exception ex)
        {
            StatusText = $"Falha ao selecionar arquivo: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task PickFolderAsync()
    {
        try
        {
            StatusText = "Compactando pasta...";
            var progress = new Progress<double>(p => UploadProgress = p);

            var item = await _folderPickerService.PickFolderAndZipAsync(progress);

            if (item is null)
            {
                StatusText = "Nenhuma pasta selecionada.";
                return;
            }

            SelectedItem = item;
            StatusText = $"Pasta compactada: {item.FileName}";
        }
        catch (Exception ex)
        {
            StatusText = $"Falha ao compactar pasta: {ex.Message}";
        }
        finally
        {
            UploadProgress = 0;
        }
    }

    [RelayCommand]
    private async Task UploadAsync()
    {
        if (SelectedItem is null || IsUploading)
            return;

        _uploadCancellation = new CancellationTokenSource();
        IsUploading = true;
        UploadProgress = 0;
        StatusText = "Enviando...";

        try
        {
            var progress = new Progress<double>(p =>
            {
                UploadProgress = p;
                StatusText = $"Enviando... {p:P0}";
            });

            var result = await _fileUploadService.UploadAsync(SelectedItem, progress, _uploadCancellation.Token);

            StatusText = $"Upload concluído. Expira em {result.ExpiresAt:g}.";
            SelectedItem = null;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Upload cancelado.";
        }
        catch (Exception ex)
        {
            StatusText = $"Falha no upload: {ex.Message}";
        }
        finally
        {
            IsUploading = false;
            _uploadCancellation = null;
        }
    }

    [RelayCommand]
    private void CancelUpload() => _uploadCancellation?.Cancel();
}
