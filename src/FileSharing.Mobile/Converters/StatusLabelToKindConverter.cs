using System.Globalization;

namespace FileSharing.Mobile.Converters;

/// <summary>Maps FileItemViewModel.StatusLabel's display text back to a StatusBadge.Kind key —
/// kept as a separate small converter (rather than exposing a "Kind" property directly on
/// FileItemViewModel) since Core has no reason to know StatusBadge's specific kind vocabulary.</summary>
public class StatusLabelToKindConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (value as string) switch
    {
        "Ativo" => "active",
        "Processando" => "pending",
        "Expirado" => "expired",
        _ => "neutral"
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
