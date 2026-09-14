namespace FileSharing.Mobile.Core.Services.ApiClient;

/// <summary>
/// The API's base URL is the only piece of "configuration" this app has — and it is explicitly
/// NOT a secret (Fase 13 §29): knowing where the API lives grants no access to anything, unlike
/// an AWS credential or a JWT signing key, neither of which this app is ever issued. Bound from
/// Resources/Raw/appsettings.json (FileSharing.Mobile) at startup, the same idea as
/// FileSharing.Web's own Api:BaseUrl appsettings.json entry — a single file to edit for a new
/// environment, never a value hardcoded inline at each call site.
/// </summary>
public class ApiClientOptions
{
    public required string BaseUrl { get; init; }
    public required string HubUrl { get; init; }
}
