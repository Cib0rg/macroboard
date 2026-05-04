using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MacroKeyboard.UI.Converters;

/// <summary>
/// Converter that returns true when two object references are equal.
/// Used to show config panel under the selected button.
/// Parameters: value[0] = current button's ButtonConfig, value[1] = ButtonConfigViewModel?.ButtonConfig
/// </summary>
public class ObjectEqualityConverter : IMultiValueConverter
{
    public static readonly ObjectEqualityConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
            return false;
        
        return values[0] != null && ReferenceEquals(values[0], values[1]);
    }
}
