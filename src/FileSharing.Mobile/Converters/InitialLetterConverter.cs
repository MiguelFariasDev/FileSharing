using System.Globalization;

namespace FileSharing.Mobile.Converters;

/// <summary>Maps an email (or any string) to its upper-cased first letter, for the Profile screen's avatar placeholder.</summary>
public class InitialLetterConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } text ? text[..1].ToUpperInvariant() : "?";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
