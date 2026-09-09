using Microsoft.Extensions.DependencyInjection;

namespace FileSharing.Mobile;

public partial class App : IApplication
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}