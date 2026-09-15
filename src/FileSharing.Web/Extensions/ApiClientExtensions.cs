using FileSharing.Web.Models;
using FileSharing.Web.Services;
using Microsoft.Extensions.Options;

namespace FileSharing.Web.Extensions;

public static class ApiClientExtensions
{
    public static IServiceCollection AddApiClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ApiSettings>(configuration.GetSection(ApiSettings.SectionName));

        // The only HttpClient in this project — every Api call goes through FileSharingApiClient.
        services.AddHttpClient<FileSharingApiClient>((sp, client) =>
        {
            var apiSettings = sp.GetRequiredService<IOptions<ApiSettings>>().Value;

            if (string.IsNullOrWhiteSpace(apiSettings.BaseUrl))
                throw new InvalidOperationException("Api:BaseUrl não configurada. Configure appsettings.{Environment}.json.");

            client.BaseAddress = new Uri(apiSettings.BaseUrl);
        });

        return services;
    }
}
