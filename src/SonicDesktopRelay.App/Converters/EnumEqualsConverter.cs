using System.Globalization;
using Avalonia.Data.Converters;

namespace SonicDesktopRelay.App.Converters;

/// <summary>
/// True when the bound value equals the converter parameter. The nav rail needs to show one
/// page out of five, and an enum cannot select a DataTemplate the way a type can.
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public static EnumEqualsConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null
        && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Page selection is one-way; the buttons set the page directly.");
}
