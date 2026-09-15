using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Core.Services.SignalR;
using FileSharing.Mobile.Views;

namespace FileSharing.Mobile;

public partial class AppShell : Shell
{
    public AppShell(AuthSession authSession, INotificationService notificationService)
    {
        InitializeComponent();

        Routing.RegisterRoute("register", typeof(RegisterPage));
        Routing.RegisterRoute("forgotpassword", typeof(ForgotPasswordPage));
        Routing.RegisterRoute("resetpassword", typeof(ResetPasswordPage));
        Routing.RegisterRoute("filedetails", typeof(FileDetailsPage));
        Routing.RegisterRoute("history", typeof(HistoryPage));
        Routing.RegisterRoute("profile", typeof(ProfilePage));
        Routing.RegisterRoute("settings", typeof(SettingsPage));

        _ = InitializeAsync(authSession, notificationService);
    }

    /// <summary>
    /// Restores a previous session (if any) before the user sees anything — Login is the first
    /// ShellContent declared in AppShell.xaml, so an unauthenticated launch already lands there
    /// with no extra work; this only needs to actively redirect past it when a valid session
    /// was found in SecureStorage (Fase 13 §4's "restauração da sessão").
    /// </summary>
    private async Task InitializeAsync(AuthSession authSession, INotificationService notificationService)
    {
        await authSession.RestoreAsync();

        if (authSession.IsAuthenticated)
        {
            _ = notificationService.StartAsync();
            await GoToAsync("//home");
        }
    }
}
