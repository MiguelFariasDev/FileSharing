using System.Globalization;

namespace FileSharing.Mobile.Converters;

/// <summary>
/// Maps FileItemViewModel.IconKey to a vendored Tabler Icons asset name (Resources/Images/,
/// rasterized at build time by the MAUI resizetizer) — replaces the previous emoji-based
/// IconKeyToEmojiConverter now that the product has a real icon system (Fase de navegação §6/§33).
/// </summary>
public class IconKeyToImageSourceConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (value as string) switch
    {
        "pdf" => "icon_file_type_pdf.svg",
        "epub" => "icon_file.svg",
        "image" => "icon_photo.svg",
        "video" => "icon_video.svg",
        "audio" => "icon_music.svg",
        "folder" => "icon_folder.svg",
        _ => "icon_file.svg"
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
