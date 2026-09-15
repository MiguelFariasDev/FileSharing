using FileSharing.Mobile.Core.Services.SignalR;

namespace FileSharing.Mobile.Extensions;

public static class NotificationExtensions
{
    public static IServiceCollection AddNotificationServices(this IServiceCollection services)
    {
        services.AddSingleton<INotificationService, SignalRNotificationService>();

        return services;
    }
}
