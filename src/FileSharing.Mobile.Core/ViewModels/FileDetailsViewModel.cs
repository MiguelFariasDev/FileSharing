using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Platform;

namespace FileSharing.Mobile.Core.ViewModels;

/// <summary>
/// Backs the file actions bottom sheet (Fase 13 §13). Every action here maps to an endpoint
/// that already exists (POST /api/files/{id}/link for generate/regenerate, GET
/// /api/files/{id}/downloads for history) — deliberately no "Excluir" action, since there is no
/// delete-file endpoint anywhere in the API (files only ever leave Active via the Fase 6
/// expiration job); adding a delete button here would be exactly the kind of UI-only feature
/// Fase 13 §13 explicitly forbids.
///
/// GeneratedLink only ever holds a value obtained THIS session, in memory — never persisted,
/// same discipline as FileSharing.Web's Dashboard/FileLinkCell (Fase 8): the API never returns
/// the plaintext token again after the call that created it, so "already has a public link"
/// (FileItemViewModel.HasPublicLink) and "we currently have a copyable/shareable link value" are
/// two different, independently-tracked things.
/// </summary>
public partial class FileDetailsViewModel : ObservableObject
{
    private readonly FileSharingApiClient _apiClient;
    private readonly IClipboardService _clipboardService;
    private readonly IShareService _shareService;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private FileItemViewModel? file;

    [ObservableProperty]
    private string? generatedLink;

    [ObservableProperty]
    private bool showRegenerateConfirm;

    [ObservableProperty]
    private bool linkBusy;

    [ObservableProperty]
    private string? errorMessage;

    public FileDetailsViewModel(FileSharingApiClient apiClient, IClipboardService clipboardService, IShareService shareService, INavigationService navigation)
    {
        _apiClient = apiClient;
        _clipboardService = clipboardService;
        _shareService = shareService;
        _navigation = navigation;
    }

    public void Initialize(FileItemViewModel file)
    {
        File = file;
        GeneratedLink = null;
        ShowRegenerateConfirm = false;
        ErrorMessage = null;
    }

    // An async Task method (not void + a fire-and-forget call) specifically so the generated
    // command is an IAsyncRelayCommand with a real ExecuteAsync — both the View's own bindings
    // and tests can then await the whole click deterministically instead of racing LinkBusy/
    // GeneratedLink updates against an un-awaited background call.
    [RelayCommand]
    private Task GenerateOrRegenerateClickedAsync()
    {
        if (File is null)
            return Task.CompletedTask;

        // A first-time link needs no confirmation; regenerating one that already exists does,
        // because it silently invalidates whatever link was already shared — same UX rule as
        // FileSharing.Web's FileLinkCell.
        if (File.HasPublicLink)
        {
            ShowRegenerateConfirm = true;
            return Task.CompletedTask;
        }

        return GenerateLinkAsync();
    }

    [RelayCommand]
    private Task ConfirmRegenerateAsync()
    {
        ShowRegenerateConfirm = false;
        return GenerateLinkAsync();
    }

    [RelayCommand]
    private void CancelRegenerate() => ShowRegenerateConfirm = false;

    private async Task GenerateLinkAsync()
    {
        if (File is null || LinkBusy)
            return;

        LinkBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _apiClient.GenerateLinkAsync(File.FileId);
            if (!result.IsSuccess)
            {
                ErrorMessage = result.Message ?? "Não foi possível gerar o link.";
                return;
            }

            GeneratedLink = result.Value!.PublicUrl;
        }
        finally
        {
            LinkBusy = false;
        }
    }

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
    private Task ViewHistoryAsync() =>
        File is null ? Task.CompletedTask : _navigation.GoToAsync($"history?fileId={File.FileId}");

    [RelayCommand]
    private Task CloseAsync() => _navigation.GoBackAsync();
}
