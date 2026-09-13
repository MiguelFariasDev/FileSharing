using FileSharing.Api.Hubs;
using FileSharing.Application.Abstractions.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace FileSharing.Api.Extensions;

public static class NotificationsExtensions
{
    public static IServiceCollection AddNotifications(this IServiceCollection services)
    {
        services.AddSignalR();

        // Required so Clients.User(...) addresses connections by the JWT "sub" claim instead
        // of SignalR's default ClaimTypes.NameIdentifier lookup — see SubClaimUserIdProvider.
        services.AddSingleton<IUserIdProvider, SubClaimUserIdProvider>();

        services.AddScoped<IFileDownloadNotifier, SignalRFileDownloadNotifier>();

        return services;
    }
}
