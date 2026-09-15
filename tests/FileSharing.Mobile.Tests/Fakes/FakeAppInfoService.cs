using FileSharing.Mobile.Core.Services.Platform;

namespace FileSharing.Mobile.Tests.Fakes;

public class FakeAppInfoService : IAppInfoService
{
    public string VersionString { get; set; } = "1.0.0";
}
