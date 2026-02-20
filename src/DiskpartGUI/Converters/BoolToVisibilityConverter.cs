using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DiskpartGUI.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var boolValue = value switch
        {
            bool b => b,
            int i => i != 0,
            null => false,
            _ => true,   // non-null object (e.g. SelectedDisk, SelectedPartition) → true
        };
        if (Invert) boolValue = !boolValue;
        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}
