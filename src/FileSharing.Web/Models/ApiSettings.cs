namespace FileSharing.Web.Models;

/// <summary>
/// Where the Api lives — the only thing that differs between Development/Production, per
/// appsettings.{Environment}.json. Never hardcoded in a component; every HTTP call goes
/// through FileSharingApiClient, which is the only consumer of this.
/// </summary>
public class ApiSettings
{
    public const string SectionName = "Api";

    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Same origin as <see cref="BaseUrl"/>, but for the SignalR endpoint
    /// (/hubs/notifications) — kept as a separate property (not string-concatenated ad hoc in
    /// each caller) so the one place that builds the hub URL is SignalRNotificationService.
    /// </summary>
    public string NotificationsHubUrl => $"{BaseUrl.TrimEnd('/')}/hubs/notifications";
}
