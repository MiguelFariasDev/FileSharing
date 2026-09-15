using FileSharing.Mobile.Views;

namespace FileSharing.Mobile.Extensions;

public static class ViewExtensions
{
    public static IServiceCollection AddViews(this IServiceCollection services)
    {
        services.AddTransient<LoginPage>();
        services.AddTransient<RegisterPage>();
        services.AddTransient<ForgotPasswordPage>();
        services.AddTransient<ResetPasswordPage>();

        // One instance per tab for the whole app session — see ViewModelExtensions' remarks on
        // why a TabBar's realized content is effectively a singleton regardless of what's
        // registered here, so this just states that lifetime honestly.
        services.AddSingleton<HomePage>();
        services.AddSingleton<FilesPage>();
        services.AddSingleton<UploadPage>();
        services.AddSingleton<ActivityPage>();

        services.AddTransient<FileDetailsPage>();
        services.AddTransient<HistoryPage>();
        services.AddTransient<ProfilePage>();
        services.AddTransient<SettingsPage>();

        services.AddSingleton<AppShell>();

        return services;
    }
}
