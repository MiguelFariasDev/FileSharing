using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Core.Services.Storage;
using FileSharing.Mobile.Services.Storage;

namespace FileSharing.Mobile.Extensions;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddAuthenticationServices(this IServiceCollection services)
    {
        services.AddSingleton<ISecureStorageService, SecureStorageService>();
        services.AddSingleton<AuthSession>();

        return services;
    }
}
