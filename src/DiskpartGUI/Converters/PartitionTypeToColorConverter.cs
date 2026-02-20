using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DiskpartGUI.Converters;

[ValueConversion(typeof(string), typeof(Brush))]
public sealed class PartitionTypeToColorConverter : IValueConverter
{
    private static readonly Brush NtfsBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0x80, 0xED));
    private static readonly Brush Fat32Brush = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));
    private static readonly Brush ExFatBrush = new SolidColorBrush(Color.FromRgb(0x8E, 0x44, 0xAD));
    private static readonly Brush SystemBrush = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
    private static readonly Brush UnknownBrush = new SolidColorBrush(Color.FromRgb(0x95, 0xA5, 0xA6));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var type = value?.ToString() ?? string.Empty;

        if (type.Contains("IFS", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("NTFS", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("Basic Data", StringComparison.OrdinalIgnoreCase))
            return NtfsBrush;

        if (type.Contains("FAT32", StringComparison.OrdinalIgnoreCase))
            return Fat32Brush;

        if (type.Contains("exFAT", StringComparison.OrdinalIgnoreCase))
            return ExFatBrush;

        if (type.Contains("System", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("OEM", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("Recovery", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("EFI", StringComparison.OrdinalIgnoreCase))
            return SystemBrush;

        return UnknownBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
