using System.Globalization;

namespace Mesh.Mobile.Converters;

public sealed class RssiToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int rssi
            ? rssi switch
            {
                > -65 => Color.FromArgb("#16A34A"),
                > -75 => Color.FromArgb("#65A30D"),
                > -85 => Color.FromArgb("#D97706"),
                _     => Color.FromArgb("#DC2626"),
            }
            : Colors.Gray;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
