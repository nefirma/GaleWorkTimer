using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using GaleWorkTimer.Domain;

namespace GaleWorkTimer.Views;

public sealed class TimerAccentBrushConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count != 2 || values[0] is not string key || values[1] is not ThemeVariant theme)
            return AvaloniaProperty.UnsetValue;
        var color = TimerPalette.Get(key);
        var dark = theme == ThemeVariant.Dark;
        var hex = parameter is "Foreground" ? (dark ? color.DarkForeground : color.LightForeground)
            : (dark ? color.Dark : color.Light);
        return new SolidColorBrush(Color.Parse(hex));
    }
}
