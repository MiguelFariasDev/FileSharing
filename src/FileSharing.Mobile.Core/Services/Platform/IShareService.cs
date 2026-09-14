namespace FileSharing.Mobile.Core.Services.Platform;

/// <summary>Wraps the Android native share sheet (Microsoft.Maui.ApplicationModel.DataTransfer.Share)
/// — Fase 13 §19 asks specifically for the OS share mechanism, not a hand-rolled share UI.</summary>
public interface IShareService
{
    Task ShareTextAsync(string title, string text);
}
