using FileSharing.Mobile.Core.ViewModels;

namespace FileSharing.Mobile.Views;

public partial class FilesPage : ContentPage
{
    private readonly FilesViewModel _viewModel;

    public FilesPage(FilesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.EnsureLoadedAsync();
    }
}
