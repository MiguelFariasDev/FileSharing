using FileSharing.Mobile.Views;

namespace FileSharing.Mobile.Extensions;

public static class ViewExtensions
{
    public static IServiceCollection AddViews(this IServiceCollection services)
    {
        services.AddTransient<LoginPage>();
        services.AddTransient<RegisterPage>();
        services.AddSingleton<HomePage>();
        services.AddTransient<UploadPage>();
        services.AddTransient<FileDetailsPage>();
        services.AddTransient<HistoryPage>();

        services.AddSingleton<AppShell>();

        return services;
    }
}
