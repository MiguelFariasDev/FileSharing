using FileSharing.Mobile.Core.ViewModels;

namespace FileSharing.Mobile.Views;

/// <summary>
/// Receives "fileId" as a Shell route query parameter (HomeViewModel.OpenFileDetailsCommand
/// navigates to "filedetails?fileId={id}") and resolves the actual FileItemViewModel from the
/// already-loaded HomeViewModel.Files — HomeViewModel is a singleton (see MauiProgram), so this
/// never needs a redundant API call just to find a file the Home list already has in memory.
/// </summary>
public partial class FileDetailsPage : ContentPage, IQueryAttributable
{
    private readonly FileDetailsViewModel _viewModel;
    private readonly HomeViewModel _homeViewModel;

    public FileDetailsPage(FileDetailsViewModel viewModel, HomeViewModel homeViewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _homeViewModel = homeViewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("fileId", out var raw) &&
            Guid.TryParse(raw?.ToString(), out var fileId))
        {
            var file = _homeViewModel.Files.FirstOrDefault(f => f.FileId == fileId);
            if (file is not null)
                _viewModel.Initialize(file);
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Slides up from below the fold rather than just popping in — the one animated
        // flourish this sheet needs (Fase 13 §13's "deve possuir animação").
        SheetBorder.TranslationY = 400;
        await SheetBorder.TranslateToAsync(0, 0, 220, Easing.CubicOut);
    }

    private async void OnScrimTapped(object? sender, TappedEventArgs e) =>
        await Shell.Current.GoToAsync("..");

    private void OnSheetTapped(object? sender, TappedEventArgs e)
    {
        // Intentionally empty — its only job is to stop the tap from reaching OnScrimTapped.
    }
}
