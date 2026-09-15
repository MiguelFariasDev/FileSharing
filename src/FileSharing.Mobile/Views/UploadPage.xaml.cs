using FileSharing.Mobile.Core.ViewModels;

namespace FileSharing.Mobile.Views;

public partial class UploadPage : ContentPage
{
    private readonly UploadViewModel _viewModel;

    public UploadPage(UploadViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Upload is now a bottom-nav tab, not a freshly-pushed page each time (Fase de
        // navegação §20) — the same ViewModel instance survives switching away and back, so
        // clearing a previous result on re-entry must never discard an upload that is still
        // actually in flight (IsBusy) just because the user briefly switched tabs.
        if (!_viewModel.IsBusy)
            _viewModel.Reset();
    }
}
