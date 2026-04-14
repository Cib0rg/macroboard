using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace MacroKeyboard.UI.Converters;

/// <summary>
/// Converts boolean to color (for connection status indicator)
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isConnected)
        {
            return isConnected ? Brushes.LimeGreen : Brushes.Red;
        }
        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
