// "Application" alone is ambiguous here: this namespace (FileSharing.Mobile.Components) nests
// under the root "FileSharing" namespace, which also directly contains the "FileSharing.Application"
// project's namespace — C#'s enclosing-namespace lookup finds that sibling before the global
// `using Microsoft.Maui.Controls;` MAUI's SDK adds implicitly, so the MAUI type needs an alias.
using MauiApplication = Microsoft.Maui.Controls.Application;

namespace FileSharing.Mobile.Components;

public partial class StatusBadge : ContentView
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(StatusBadge), string.Empty);

    public static readonly BindableProperty KindProperty =
        BindableProperty.Create(nameof(Kind), typeof(string), typeof(StatusBadge), "neutral", propertyChanged: OnKindChanged);

    public static readonly BindableProperty DotColorProperty =
        BindableProperty.Create(nameof(DotColor), typeof(Color), typeof(StatusBadge));

    public static readonly BindableProperty TextColorProperty =
        BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(StatusBadge));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>One of "active", "pending", "expired" — anything else falls back to neutral.</summary>
    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public Color? DotColor
    {
        get => (Color?)GetValue(DotColorProperty);
        private set => SetValue(DotColorProperty, value);
    }

    public Color? TextColor
    {
        get => (Color?)GetValue(TextColorProperty);
        private set => SetValue(TextColorProperty, value);
    }

    public StatusBadge()
    {
        InitializeComponent();
        ApplyKind(Kind);
    }

    private static void OnKindChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((StatusBadge)bindable).ApplyKind((string)newValue);

    private void ApplyKind(string kind)
    {
        var (dot, text) = kind switch
        {
            "active" => ((Color)MauiApplication.Current!.Resources["Success"], (Color)MauiApplication.Current!.Resources["Success"]),
            "pending" => ((Color)MauiApplication.Current!.Resources["Warning"], (Color)MauiApplication.Current!.Resources["Warning"]),
            "expired" => ((Color)MauiApplication.Current!.Resources["Gray400"], (Color)MauiApplication.Current!.Resources["Gray500"]),
            _ => ((Color)MauiApplication.Current!.Resources["Gray400"], (Color)MauiApplication.Current!.Resources["Gray600"])
        };

        DotColor = dot;
        TextColor = text;
    }
}
