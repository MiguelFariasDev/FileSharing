namespace FileSharing.Api.Hubs;

/// <summary>
/// Single source of truth for the notifications hub's route — shared between where it's
/// mapped (Program.cs) and where JWT-over-query-string is scoped to it (AuthExtensions), so the
/// two never drift apart.
/// </summary>
public static class HubEndpoints
{
    public const string Notifications = "/hubs/notifications";
}
