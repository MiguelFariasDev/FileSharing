using System.Text.Json;
using FileSharing.Mobile.Core.Services.ApiClient;

namespace FileSharing.Mobile.Extensions;

public static class ApiClientExtensions
{
    public static IServiceCollection AddApiClientServices(this IServiceCollection services)
    {
        var apiOptions = LoadApiClientOptions();
        services.AddSingleton(apiOptions);

        // Its own HttpClient, distinct from FileUploadService's bare one (see that class's own
        // remarks on why the presigned-URL PUT must never reuse this client/base
        // address/any Authorization header).
        services.AddSingleton(_ => new HttpClient { BaseAddress = new Uri(apiOptions.BaseUrl) });
        services.AddSingleton<FileSharingApiClient>();

        return services;
    }

    /// <summary>
    /// Resources/Raw/appsettings.json is the single, explicit place to point this app at a
    /// different API/Hub URL (a different emulator alias, a physical device's LAN IP, a real
    /// deployed environment) — never a value hardcoded inline at each call site, and never a
    /// place a secret belongs (the API base URL is not a secret — see docs/mobile.md).
    /// Read synchronously via .GetAwaiter().GetResult() because MauiApp.CreateBuilder() itself
    /// is synchronous and this must complete before any service that depends on it is
    /// registered; this only ever runs once, at process startup.
    /// </summary>
    private static ApiClientOptions LoadApiClientOptions()
    {
        using var stream = FileSystem.OpenAppPackageFileAsync("appsettings.json").GetAwaiter().GetResult();
        using var document = JsonDocument.Parse(stream);

        var api = document.RootElement.GetProperty("Api");

        return new ApiClientOptions
        {
            BaseUrl = api.GetProperty("BaseUrl").GetString()!,
            HubUrl = api.GetProperty("HubUrl").GetString()!
        };
    }
}
