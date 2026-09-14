using System.Text.Json;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Core.Services.Platform;
using FileSharing.Mobile.Core.Services.SignalR;
using FileSharing.Mobile.Core.Services.Storage;
using FileSharing.Mobile.Core.Services.Upload;
using FileSharing.Mobile.Core.ViewModels;
using FileSharing.Mobile.Services.Platform;
using FileSharing.Mobile.Services.Storage;
using FileSharing.Mobile.Services.Upload;
using FileSharing.Mobile.Views;
using Microsoft.Extensions.Logging;

namespace FileSharing.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        var apiOptions = LoadApiClientOptions();
        builder.Services.AddSingleton(apiOptions);

        // Authentication / secure storage
        builder.Services.AddSingleton<ISecureStorageService, SecureStorageService>();
        builder.Services.AddSingleton<AuthSession>();

        // API client — its own HttpClient, distinct from FileUploadService's bare one (see that
        // class's own remarks on why the presigned-URL PUT must never reuse this client/base
        // address/any Authorization header).
        builder.Services.AddSingleton(_ => new HttpClient { BaseAddress = new Uri(apiOptions.BaseUrl) });
        builder.Services.AddSingleton<FileSharingApiClient>();

        // Upload
        builder.Services.AddTransient<IFilePickerService, FilePickerService>();
        builder.Services.AddTransient<S3UploadHttpClient>(); // never the API's HttpClient — see its own remarks
        builder.Services.AddTransient<IFileUploadService, FileUploadService>();

#if ANDROID
        builder.Services.AddTransient<IFolderPickerService, Platforms.Android.FolderPickerService>();
#endif

        // SignalR
        builder.Services.AddSingleton<INotificationService, SignalRNotificationService>();

        // Platform abstractions
        builder.Services.AddSingleton<INavigationService, NavigationService>();
        builder.Services.AddSingleton<IClipboardService, ClipboardService>();
        builder.Services.AddSingleton<IShareService, ShareService>();
        builder.Services.AddSingleton<IMainThreadDispatcher, MainThreadDispatcher>();

        // ViewModels — Home is a singleton (one dashboard, one SignalR subscription, one
        // re-label timer for the whole app session); the rest are transient (a fresh state
        // every time the user navigates to Login/Register/Upload/FileDetails/History).
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<RegisterViewModel>();
        builder.Services.AddSingleton<HomeViewModel>();
        builder.Services.AddTransient<UploadViewModel>();
        builder.Services.AddTransient<FileDetailsViewModel>();
        builder.Services.AddTransient<HistoryViewModel>();

        // Views
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<RegisterPage>();
        builder.Services.AddSingleton<HomePage>();
        builder.Services.AddTransient<UploadPage>();
        builder.Services.AddTransient<FileDetailsPage>();
        builder.Services.AddTransient<HistoryPage>();

        builder.Services.AddSingleton<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    /// <summary>
    /// Resources/Raw/appsettings.json is the single, explicit place to point this app at a
    /// different API/Hub URL (a different emulator alias, a physical device's LAN IP, a real
    /// deployed environment) — never a value hardcoded inline at each call site, and never a
    /// place a secret belongs (the API base URL is not a secret — see docs/mobile.md).
    /// Read synchronously via .GetAwaiter().GetResult() because MauiApp.CreateBuilder() itself
    /// is synchronous and this must complete before any service that depends on it is
    /// registered; this only ever runs once, at process startup.
    /// </summary>
    private static ApiClientOptions LoadApiClientOptions()
    {
        using var stream = FileSystem.OpenAppPackageFileAsync("appsettings.json").GetAwaiter().GetResult();
        using var document = JsonDocument.Parse(stream);

        var api = document.RootElement.GetProperty("Api");

        return new ApiClientOptions
        {
            BaseUrl = api.GetProperty("BaseUrl").GetString()!,
            HubUrl = api.GetProperty("HubUrl").GetString()!
        };
    }
}
