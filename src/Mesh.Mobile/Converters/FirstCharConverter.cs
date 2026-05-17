namespace Mesh.Mobile.Converters;

public sealed class FirstCharConverter : IValueConverter
{
    public static readonly FirstCharConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is string s && s.Length > 0 ? $"{s[0]}".ToUpperInvariant() : "?";

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
