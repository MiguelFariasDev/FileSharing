namespace FileSharing.Mobile.Components;

[ContentProperty(nameof(InnerContent))]
public partial class GlassCard : ContentView
{
    public static readonly BindableProperty InnerContentProperty =
        BindableProperty.Create(nameof(InnerContent), typeof(View), typeof(GlassCard));

    public View InnerContent
    {
        get => (View)GetValue(InnerContentProperty);
        set => SetValue(InnerContentProperty, value);
    }

    public GlassCard()
    {
        InitializeComponent();
    }
}
