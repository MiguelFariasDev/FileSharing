using FileSharing.Mobile.Core.ViewModels;

namespace FileSharing.Mobile.Views;

public partial class ActivityPage : ContentPage
{
    public ActivityPage(ActivityViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
