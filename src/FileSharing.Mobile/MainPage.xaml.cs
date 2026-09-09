using FileSharing.Mobile.ViewModels;

namespace FileSharing.Mobile;

public partial class MainPage : ContentPage
{
    public MainPage(UploadViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
