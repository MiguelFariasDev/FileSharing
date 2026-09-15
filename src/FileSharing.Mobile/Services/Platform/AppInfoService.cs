using FileSharing.Mobile.Core.Services.Platform;

namespace FileSharing.Mobile.Services.Platform;

public class AppInfoService : IAppInfoService
{
    public string VersionString => AppInfo.Current.VersionString;
}
