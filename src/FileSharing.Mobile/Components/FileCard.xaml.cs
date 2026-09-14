using System.Windows.Input;
using FileSharing.Mobile.Core.ViewModels;

namespace FileSharing.Mobile.Components;

public partial class FileCard : ContentView
{
    public static readonly BindableProperty FileProperty =
        BindableProperty.Create(nameof(File), typeof(FileItemViewModel), typeof(FileCard));

    public static readonly BindableProperty MenuCommandProperty =
        BindableProperty.Create(nameof(MenuCommand), typeof(ICommand), typeof(FileCard));

    public FileItemViewModel? File
    {
        get => (FileItemViewModel?)GetValue(FileProperty);
        set => SetValue(FileProperty, value);
    }

    public ICommand? MenuCommand
    {
        get => (ICommand?)GetValue(MenuCommandProperty);
        set => SetValue(MenuCommandProperty, value);
    }

    public FileCard()
    {
        InitializeComponent();
    }
}
