using FileSharing.Mobile.Core.Services.Activity;
using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Core.ViewModels;

namespace FileSharing.Mobile.Extensions;

public static class ViewModelExtensions
{
    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        // Home/Files/Upload/Activity are all singletons now that they live behind a persistent
        // TabBar (Fase de navegação §20) — MAUI Shell realizes each tab's content once and
        // reuses it across tab switches, so a Transient registration here would just be a lie
        // about the actual lifetime. The rest stay Transient — a fresh state every time the user
        // navigates to Register/ForgotPassword/ResetPassword/FileDetails/History/Profile/Settings.
        services.AddTransient<LoginViewModel>();
        services.AddTransient<RegisterViewModel>();
        services.AddTransient<ForgotPasswordViewModel>();
        services.AddTransient<ResetPasswordViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<FilesViewModel>();
        services.AddSingleton<UploadViewModel>();
        services.AddSingleton<ActivityViewModel>();
        services.AddTransient<FileDetailsViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<ProfileViewModel>();
        services.AddTransient<SettingsViewModel>();

        services.AddSingleton<ActivityFeedService>();
        services.AddSingleton<LogoutService>();

        return services;
    }
}
