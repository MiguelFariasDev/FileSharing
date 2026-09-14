using CommunityToolkit.Mvvm.ComponentModel;
using FileSharing.Application.DTOs.Files;

namespace FileSharing.Mobile.Core.ViewModels;

/// <summary>
/// Presentation wrapper around one FileSummaryResponse row (GET /api/files/mine) — computes
/// everything the FileCard/bottom sheet need to display without the View ever touching the raw
/// DTO's Status string or ExpiresAt directly. Mirrors FileSharing.Web's Dashboard.razor
/// FileViewModel/RemainingTimeLabel logic (Fase 8/9) so both clients agree on what "active",
/// "expired" and the countdown text mean for the exact same server-reported state.
/// </summary>
public partial class FileItemViewModel : ObservableObject
{
    public FileItemViewModel(FileSummaryResponse summary)
    {
        Summary = summary;
    }

    public FileSummaryResponse Summary { get; private set; }

    public Guid FileId => Summary.FileId;
    public string OriginalFileName => Summary.OriginalFileName;
    public string ContentType => Summary.ContentType;
    public bool IsFolder => Summary.IsFolder;
    public int DownloadCount => Summary.DownloadCount;
    public bool HasPublicLink => Summary.HasPublicLink;

    /// <summary>Server-confirmed Expired (Hangfire already ran) reads the same as a still-Active
    /// file whose ExpiresAt the client's own clock has already passed — never trust "Active" by
    /// itself once ExpiresAt is in the past (same reasoning as the Web dashboard's
    /// IsEffectivelyExpired).</summary>
    public bool IsEffectivelyExpired =>
        Summary.Status == "Expired" ||
        (Summary.Status == "Active" && Summary.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow);

    public bool IsPending => Summary.Status == "PendingUpload";
    public bool IsActive => !IsPending && !IsEffectivelyExpired;

    public string StatusLabel => Summary.Status switch
    {
        "PendingUpload" => "Processando",
        _ when IsEffectivelyExpired => "Expirado",
        "Active" => "Ativo",
        _ => Summary.Status
    };

    public string FormattedSize => Summary.SizeBytes switch
    {
        < 1024 => $"{Summary.SizeBytes} B",
        < 1024 * 1024 => $"{Summary.SizeBytes / 1024.0:0.#} KB",
        < 1024 * 1024 * 1024 => $"{Summary.SizeBytes / (1024.0 * 1024):0.#} MB",
        _ => $"{Summary.SizeBytes / (1024.0 * 1024 * 1024):0.#} GB"
    };

    public string TypeLabel => IsFolder ? "ZIP" : ContentType switch
    {
        "application/pdf" => "PDF",
        "application/epub+zip" => "EPUB",
        "application/zip" => "ZIP",
        _ when ContentType.StartsWith("image/") => "Imagem",
        _ when ContentType.StartsWith("video/") => "Vídeo",
        _ when ContentType.StartsWith("audio/") => "Áudio",
        _ => "Arquivo"
    };

    /// <summary>Minimalist glyph key the View maps to an actual icon/font glyph — kept as data
    /// here (not a MAUI-specific type) so this class has no UI dependency.</summary>
    public string IconKey => IsFolder ? "folder" : ContentType switch
    {
        "application/pdf" => "pdf",
        "application/epub+zip" => "epub",
        _ when ContentType.StartsWith("image/") => "image",
        _ when ContentType.StartsWith("video/") => "video",
        _ when ContentType.StartsWith("audio/") => "audio",
        _ => "file"
    };

    /// <summary>Always computed from the server's own ExpiresAt (Fase 13 §21) — never a local
    /// timer started at upload time, and never allowed to read "active" once ExpiresAt has
    /// actually passed, regardless of what Status currently says.</summary>
    public string RemainingLabel
    {
        get
        {
            if (IsPending)
                return "—";

            if (IsEffectivelyExpired)
                return "Expirado";

            if (Summary.ExpiresAt is not { } expiresAt)
                return "—";

            var remaining = expiresAt - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return "Expirado";

            if (remaining < TimeSpan.FromMinutes(1))
                return "menos de 1 min restante";

            return remaining.TotalHours >= 1
                ? $"Expira em {(int)remaining.TotalHours}h {remaining.Minutes}min"
                : $"Expira em {remaining.Minutes}min";
        }
    }

    /// <summary>Called when GET /api/files/mine is refreshed (pull-to-refresh, periodic
    /// re-label tick) — replaces the wrapped DTO and re-raises property-changed for every
    /// computed property above so the bound FileCard actually redraws.</summary>
    public void UpdateFrom(FileSummaryResponse summary)
    {
        Summary = summary;
        RaiseAllChanged();
    }

    /// <summary>Called by the SignalR FileDownloaded handler — a download increments the count
    /// without needing a full list reload.</summary>
    public void IncrementDownloadCount()
    {
        Summary = Summary with { DownloadCount = Summary.DownloadCount + 1 };
        OnPropertyChanged(nameof(DownloadCount));
    }

    /// <summary>Called by the local re-label timer (Home) — only the time-derived properties
    /// actually change on a tick, so only those are re-raised.</summary>
    public void RefreshRemainingLabel()
    {
        OnPropertyChanged(nameof(RemainingLabel));
        OnPropertyChanged(nameof(IsEffectivelyExpired));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(StatusLabel));
    }

    private void RaiseAllChanged()
    {
        OnPropertyChanged(nameof(OriginalFileName));
        OnPropertyChanged(nameof(ContentType));
        OnPropertyChanged(nameof(IsFolder));
        OnPropertyChanged(nameof(DownloadCount));
        OnPropertyChanged(nameof(HasPublicLink));
        OnPropertyChanged(nameof(IsEffectivelyExpired));
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(FormattedSize));
        OnPropertyChanged(nameof(TypeLabel));
        OnPropertyChanged(nameof(IconKey));
        OnPropertyChanged(nameof(RemainingLabel));
    }
}
