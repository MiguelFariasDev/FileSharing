using System.Globalization;

namespace FileSharing.Mobile.Converters;

/// <summary>
/// Maps FileItemViewModel.IconKey to a plain emoji glyph — rendered by the system font on every
/// Android device with zero bundled-font risk, deliberately instead of a custom icon font (Fase
/// 13 §12 only asks for "ícone apropriado por tipo", not pixel-perfect iconography).
/// </summary>
public class IconKeyToEmojiConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (value as string) switch
    {
        "pdf" => "📄",
        "epub" => "📚",
        "image" => "🖼",
        "video" => "🎬",
        "audio" => "🎵",
        "folder" => "🗂",
        _ => "📁"
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
