using FileSharing.Web.Models;
using FileSharing.Web.Services;
using Microsoft.Extensions.Options;

namespace FileSharing.Web.Extensions;

public static class ApiClientExtensions
{
    public static IServiceCollection AddApiClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ApiSettings>(configuration.GetSection(ApiSettings.SectionName));

        // Every call to this app's own Api goes through FileSharingApiClient's HttpClient.
        services.AddHttpClient<FileSharingApiClient>((sp, client) =>
        {
            var apiSettings = sp.GetRequiredService<IOptions<ApiSettings>>().Value;

            if (string.IsNullOrWhiteSpace(apiSettings.BaseUrl))
                throw new InvalidOperationException("Api:BaseUrl não configurada. Configure appsettings.{Environment}.json.");

            client.BaseAddress = new Uri(apiSettings.BaseUrl);
        });

        // Deliberately separate and unconfigured (no BaseAddress, no default headers): the
        // upload flow's step 2 PUTs straight to a presigned S3 URL, which already carries its
        // own authorization — this client must never see the Api's bearer token or base address.
        services.AddHttpClient<S3UploadHttpClient>();

        return services;
    }
}
