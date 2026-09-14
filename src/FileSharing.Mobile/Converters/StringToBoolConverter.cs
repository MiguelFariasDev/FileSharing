using System.Globalization;

namespace FileSharing.Mobile.Converters;

/// <summary>True when the bound string is non-empty — used to show/hide an error/success
/// message Label without a separate "HasX" boolean property on every ViewModel.</summary>
public class StringToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
