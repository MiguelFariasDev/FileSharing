namespace FileSharing.Mobile.Extensions;

public static class ServiceRegistrationExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddApiClientServices();
        services.AddUploadServices();
        services.AddNotificationServices();
        services.AddPlatformServices();

        return services;
    }

    public static IServiceCollection AddPresentationServices(this IServiceCollection services)
    {
        services.AddViewModels();
        services.AddViews();

        return services;
    }
}
