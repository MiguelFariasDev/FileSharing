using FileSharing.Application.DTOs.Files;

namespace FileSharing.Web.Services;

/// <summary>
/// Pure display-formatting helpers shared by Dashboard.razor and FileDetails.razor — extracted
/// so the two pages agree on status labels/countdown/size formatting instead of drifting
/// independently. Never used to allow/deny anything; the Api's own ExpiresAt/Status are always
/// the source of truth (see IsEffectivelyExpired remarks).
/// </summary>
public static class FileDisplayFormatting
{
    // ExpiresAt/Status keep coming from the Api — this only changes display text the instant
    // the client's own clock crosses ExpiresAt, ahead of the ~15-minute Hangfire cleanup window
    // that eventually flips Status to Expired server-side. Never used to allow/deny anything —
    // link generation still calls the real endpoint, which still enforces this itself
    // regardless of what this method displays.
    public static bool IsEffectivelyExpired(FileSummaryResponse file) =>
        file.Status == "Active" && file.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow;

    public static string StatusLabel(FileSummaryResponse file) => file.Status switch
    {
        "PendingUpload" => "Processando",
        "Active" when IsEffectivelyExpired(file) => "Expirado",
        "Active" => "Ativo",
        "Expired" => "Expirado",
        _ => file.Status
    };

    public static string StatusBadgeClass(FileSummaryResponse file) => file.Status switch
    {
        "PendingUpload" => "text-bg-secondary",
        "Active" when IsEffectivelyExpired(file) => "text-bg-danger",
        "Active" => "text-bg-success",
        "Expired" => "text-bg-danger",
        _ => "text-bg-secondary"
    };

    public static string RemainingTimeLabel(FileSummaryResponse file)
    {
        // Server-confirmed Expired (Hangfire already ran) reads the same as a still-Active file
        // whose ExpiresAt the client's own clock has already passed — the countdown's natural
        // end-state is "Expirado" either way, never a bare "—" once a file has genuinely expired.
        if (file.Status == "Expired")
            return "Expirado";

        if (file.Status != "Active" || file.ExpiresAt is not { } expiresAt)
            return "—";

        var remaining = expiresAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return "Expirado";

        if (remaining < TimeSpan.FromMinutes(1))
            return "menos de 1 min restante";

        return remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours}h {remaining.Minutes}min restantes"
            : $"{remaining.Minutes}min restantes";
    }

    public static string FormatDate(DateTimeOffset? date) => date?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "—";

    public static string FormatDateTime(DateTimeOffset date) => date.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.#} {units[unitIndex]}";
    }
}
