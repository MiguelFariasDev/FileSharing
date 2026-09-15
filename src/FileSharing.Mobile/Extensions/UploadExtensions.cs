using FileSharing.Mobile.Core.Services.Upload;
using FileSharing.Mobile.Services.Upload;

namespace FileSharing.Mobile.Extensions;

public static class UploadExtensions
{
    public static IServiceCollection AddUploadServices(this IServiceCollection services)
    {
        services.AddTransient<IFilePickerService, FilePickerService>();
        services.AddTransient<S3UploadHttpClient>(); // never the API's HttpClient — see its own remarks
        services.AddTransient<IFileUploadService, FileUploadService>();

#if ANDROID
        services.AddTransient<IFolderPickerService, FileSharing.Mobile.Platforms.Android.FolderPickerService>();
#endif

        return services;
    }
}
