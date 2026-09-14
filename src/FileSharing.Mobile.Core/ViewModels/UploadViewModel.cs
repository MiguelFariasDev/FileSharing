using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSharing.Mobile.Core.Models;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Platform;
using FileSharing.Mobile.Core.Services.Upload;

namespace FileSharing.Mobile.Core.ViewModels;

/// <summary>
/// Drives the dedicated upload flow (Fase 13 §14): pick -> preview -> confirm -> initiate ->
/// PUT to S3 (real, byte-driven progress — see FileUploadService/ProgressReportingStream) ->
/// complete -> generate the public link -> show it. No automatic retry (see FileUploadService's
/// own remarks) — a failure leaves SelectedItem in place so ConfirmUploadCommand can simply be
/// invoked again, which correctly starts a brand new upload from scratch.
/// </summary>
public partial class UploadViewModel : ObservableObject
{
    private readonly FileSharingApiClient _apiClient;
    private readonly IFilePickerService _filePickerService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IFileUploadService _fileUploadService;
    private readonly IClipboardService _clipboardService;
    private readonly IShareService _shareService;
    private readonly INavigationService _navigation;
    private readonly IMainThreadDispatcher _dispatcher;

    private CancellationTokenSource? _uploadCancellation;

    [ObservableProperty]
    private UploadableItem? selectedItem;

    [ObservableProperty]
    private UploadStage stage = UploadStage.Idle;

    [ObservableProperty]
    private double progressFraction;

    [ObservableProperty]
    private string progressDetailLabel = string.Empty;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? generatedLink;

    public bool HasSelection => SelectedItem is not null;
    public bool IsIdle => Stage is UploadStage.Idle or UploadStage.Failed;
    public bool IsBusy => Stage is UploadStage.Preparing or UploadStage.Uploading or UploadStage.Completing;
    public bool IsCompleted => Stage == UploadStage.Completed;
    public bool IsFailed => Stage == UploadStage.Failed;

    public string StageLabel => Stage switch
    {
        UploadStage.Preparing => "Preparando...",
        UploadStage.Uploading => "Enviando com segurança...",
        UploadStage.Completing => "Finalizando...",
        UploadStage.Completed => "Concluído",
        UploadStage.Failed => "Falhou",
        _ => string.Empty
    };

    partial void OnSelectedItemChanged(UploadableItem? value) => OnPropertyChanged(nameof(HasSelection));
    partial void OnStageChanged(UploadStage value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(StageLabel));
    }

    public UploadViewModel(
        FileSharingApiClient apiClient,
        IFilePickerService filePickerService,
        IFolderPickerService folderPickerService,
        IFileUploadService fileUploadService,
        IClipboardService clipboardService,
        IShareService shareService,
        INavigationService navigation,
        IMainThreadDispatcher dispatcher)
    {
        _apiClient = apiClient;
        _filePickerService = filePickerService;
        _folderPickerService = folderPickerService;
        _fileUploadService = fileUploadService;
        _clipboardService = clipboardService;
        _shareService = shareService;
        _navigation = navigation;
        _dispatcher = dispatcher;
    }

    /// <summary>Resets to a clean picking state — called every time the page is navigated to,
    /// so a previous upload's result never lingers if the user comes back to upload again.</summary>
    public void Reset()
    {
        SelectedItem = null;
        Stage = UploadStage.Idle;
        ProgressFraction = 0;
        ProgressDetailLabel = string.Empty;
        ErrorMessage = null;
        GeneratedLink = null;
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
            ErrorMessage = null;
        }
        catch (Exception)
        {
            ErrorMessage = "Não foi possível selecionar o arquivo.";
        }
    }

    [RelayCommand]
    private async Task PickFolderAsync()
    {
        try
        {
            var progress = new Progress<double>(p => ProgressFraction = p);
            var item = await _folderPickerService.PickFolderAndZipAsync(progress);

            ProgressFraction = 0;

            if (item is null)
                return;

            SelectedItem = item;
            ErrorMessage = null;
        }
        catch (Exception)
        {
            ErrorMessage = "Não foi possível compactar a pasta selecionada.";
        }
    }

    [RelayCommand]
    private async Task ConfirmUploadAsync()
    {
        if (SelectedItem is null || IsBusy)
            return;

        ErrorMessage = null;
        Stage = UploadStage.Preparing;
        ProgressFraction = 0;

        _uploadCancellation = new CancellationTokenSource();

        var progress = new Progress<UploadProgressUpdate>(update => _dispatcher.BeginInvokeOnMainThread(() =>
        {
            Stage = update.Stage;
            ProgressFraction = update.Fraction;
            ProgressDetailLabel = FormatBytes(update.BytesSent) + " de " + FormatBytes(update.TotalBytes);
        }));

        var outcome = await _fileUploadService.UploadAsync(SelectedItem, progress, _uploadCancellation.Token);

        if (!outcome.IsSuccess)
        {
            Stage = UploadStage.Failed;
            ErrorMessage = outcome.UserMessage;
            _uploadCancellation = null;
            return;
        }

        // The link is generated as a separate, explicit step right after the file becomes
        // Active — POST /api/files/{id}/link is the same endpoint the Web dashboard uses to
        // generate a first link, reused here rather than reimplemented.
        var linkResult = await _apiClient.GenerateLinkAsync(outcome.Result!.FileId);
        GeneratedLink = linkResult.IsSuccess ? linkResult.Value!.PublicUrl : null;

        Stage = UploadStage.Completed;
        _uploadCancellation = null;
    }

    [RelayCommand]
    private void CancelUpload() => _uploadCancellation?.Cancel();

    [RelayCommand]
    private async Task CopyLinkAsync()
    {
        if (GeneratedLink is { } link)
            await _clipboardService.SetTextAsync(link);
    }

    [RelayCommand]
    private async Task ShareLinkAsync()
    {
        if (GeneratedLink is { } link)
            await _shareService.ShareTextAsync("Compartilhar link", link);
    }

    [RelayCommand]
    private async Task DoneAsync()
    {
        Reset();
        await _navigation.GoBackAsync();
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024):0.#} MB"
    };
}
