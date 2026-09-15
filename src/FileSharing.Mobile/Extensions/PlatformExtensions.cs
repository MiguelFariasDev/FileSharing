using FileSharing.Mobile.Core.Services.Platform;
using FileSharing.Mobile.Services.Platform;

namespace FileSharing.Mobile.Extensions;

public static class PlatformExtensions
{
    public static IServiceCollection AddPlatformServices(this IServiceCollection services)
    {
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IShareService, ShareService>();
        services.AddSingleton<IMainThreadDispatcher, MainThreadDispatcher>();
        services.AddSingleton<IAppInfoService, AppInfoService>();

        return services;
    }
}
