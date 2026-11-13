using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Ascolotl.App.Converters;

/// <summary>
/// Converts an outline heading level (1-6) to a left margin for indentation.
/// Level 1 = no indent, level 2 = 16px indent, etc.
/// </summary>
public sealed class LevelToMarginConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int level)
            return new Thickness((level - 1) * 16, 2, 0, 2);

        return new Thickness(0, 2, 0, 2);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
