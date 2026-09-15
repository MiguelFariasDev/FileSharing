namespace FileSharing.Web.Extensions;

public static class ServiceRegistrationExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAppAuthentication();
        services.AddApiClient(configuration);
        services.AddAppServices();

        return services;
    }
}
