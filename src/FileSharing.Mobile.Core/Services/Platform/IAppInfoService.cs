namespace FileSharing.Mobile.Core.Services.Platform;

/// <summary>Thin abstraction over Microsoft.Maui.ApplicationModel.AppInfo — keeps the Settings screen's ViewModel testable without a real MAUI host.</summary>
public interface IAppInfoService
{
    string VersionString { get; }
}
