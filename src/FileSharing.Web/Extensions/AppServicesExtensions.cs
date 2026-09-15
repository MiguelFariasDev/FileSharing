using FileSharing.Web.Services;
using FileSharing.Web.Services.Notifications;

namespace FileSharing.Web.Extensions;

public static class AppServicesExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection services)
    {
        services.AddScoped<ToastService>();
        services.AddScoped<SignalRNotificationService>();

        return services;
    }
}
