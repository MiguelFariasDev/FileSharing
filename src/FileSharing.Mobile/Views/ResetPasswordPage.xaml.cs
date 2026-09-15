using FileSharing.Mobile.Core.ViewModels;

namespace FileSharing.Mobile.Views;

public partial class ResetPasswordPage : ContentPage, IQueryAttributable
{
    private readonly ResetPasswordViewModel _viewModel;

    public ResetPasswordPage(ResetPasswordViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        query.TryGetValue("token", out var token);
        _ = _viewModel.InitializeAsync(token?.ToString());
    }
}
