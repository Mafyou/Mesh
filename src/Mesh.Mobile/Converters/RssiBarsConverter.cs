using System.Globalization;

namespace Mesh.Mobile.Converters;

public sealed class RssiBarsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int rssi
            ? rssi switch
            {
                > -65 => "▂▄▆█",
                > -75 => "▂▄▆·",
                > -85 => "▂▄··",
                _     => "▂···",
            }
            : "····";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
