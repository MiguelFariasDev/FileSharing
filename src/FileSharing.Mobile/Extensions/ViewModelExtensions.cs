using FileSharing.Mobile.Core.ViewModels;

namespace FileSharing.Mobile.Extensions;

public static class ViewModelExtensions
{
    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        // Home is a singleton (one dashboard, one SignalR subscription, one re-label timer for
        // the whole app session); the rest are transient (a fresh state every time the user
        // navigates to Login/Register/Upload/FileDetails/History).
        services.AddTransient<LoginViewModel>();
        services.AddTransient<RegisterViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddTransient<UploadViewModel>();
        services.AddTransient<FileDetailsViewModel>();
        services.AddTransient<HistoryViewModel>();

        return services;
    }
}
