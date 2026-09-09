namespace FileSharing.Mobile.Services.Api;

public static class ApiClientOptions
{
    /// <summary>
    /// 10.0.2.2 is the Android emulator's alias for the host machine's localhost — it lets
    /// the app reach the API running via `dotnet run` on the development machine. On a
    /// physical device this must be replaced with the machine's LAN address or a real
    /// deployed API URL.
    /// </summary>
    public const string BaseUrl = "http://10.0.2.2:5105/";
}
