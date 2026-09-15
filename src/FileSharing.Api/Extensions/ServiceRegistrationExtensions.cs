namespace FileSharing.Api.Extensions;

public static class ServiceRegistrationExtensions
{
    public static IServiceCollection AddAuthenticationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthServices(configuration);
        services.AddJwtAuthentication();

        return services;
    }

    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPersistence(configuration);
        services.AddFileStorage(configuration);
        services.AddBackgroundJobs(configuration);
        services.AddNotifications();

        return services;
    }

    public static IServiceCollection AddCrossCuttingServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddErrorHandling();
        services.AddRequestLogging();
        services.AddApiRateLimiting();
        services.AddSwaggerWithJwtSupport();
        services.AddObservability(configuration);

        return services;
    }
}
