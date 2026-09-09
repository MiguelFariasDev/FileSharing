using FileSharing.Mobile.Services.Api;
using FileSharing.Mobile.Services.Upload;
using FileSharing.Mobile.ViewModels;
using Microsoft.Extensions.Logging;

namespace FileSharing.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton(_ => new HttpClient
		{
			BaseAddress = new Uri(ApiClientOptions.BaseUrl)
		});
		builder.Services.AddSingleton<FileSharingApiClient>();

		builder.Services.AddTransient<IFilePickerService, FilePickerService>();
		builder.Services.AddTransient<IFileUploadService, FileUploadService>();

#if ANDROID
		builder.Services.AddTransient<IFolderPickerService, Platforms.Android.FolderPickerService>();
#endif

		builder.Services.AddTransient<UploadViewModel>();
		builder.Services.AddTransient<MainPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
