using Avalonia.Media;
using GaleWorkTimer.Domain;
using Xunit;

namespace GaleWorkTimer.Tests;

public sealed class TimerPaletteTests
{
    [Fact]
    public void All_legacy_keys_resolve_uniquely_with_readable_deterministic_theme_foregrounds()
    {
        string[] legacy = ["blue", "purple", "cyan", "teal", "green", "yellow", "orange", "red", "pink", "gray"];
        Assert.Equal(legacy, TimerPalette.Colors.Select(c => c.ColorKey));
        Assert.Equal(10, TimerPalette.Colors.Select(c => c.ColorKey).Distinct().Count());
        Assert.Equal(10, TimerPalette.Colors.Select(c => c.Light).Distinct().Count());
        Assert.Equal(10, TimerPalette.Colors.Select(c => c.Dark).Distinct().Count());
        foreach (var key in legacy)
        {
            var color = TimerPalette.Get(key);
            Assert.False(string.IsNullOrWhiteSpace(color.Name));
            foreach (var (background, foreground) in new[] { (color.Light, color.LightForeground), (color.Dark, color.DarkForeground) })
            {
                Assert.Contains(foreground, new[] { "#000000", "#FFFFFF" });
                var rgb = Color.Parse(background);
                Assert.Equal(255, rgb.A);
                // Independent WCAG contrast check for black/white text on actual published colors.
                static double Linear(byte channel) => channel / 255d <= 0.04045
                    ? channel / 255d / 12.92 : Math.Pow((channel / 255d + 0.055) / 1.055, 2.4);
                var luminance = 0.2126 * Linear(rgb.R) + 0.7152 * Linear(rgb.G) + 0.0722 * Linear(rgb.B);
                var blackRatio = (luminance + 0.05) / 0.05;
                var whiteRatio = 1.05 / (luminance + 0.05);
                Assert.True((foreground == "#000000" ? blackRatio : whiteRatio) >= 4.5);
            }
            Assert.Equal(color.LightForeground, TimerPalette.Get(key).LightForeground);
            Assert.Equal(color.DarkForeground, TimerPalette.Get(key).DarkForeground);
        }
        Assert.Equal("#FFFFFF", TimerPalette.Get("blue").LightForeground);
        Assert.Equal("#000000", TimerPalette.Get("yellow").LightForeground);
        Assert.Throws<ArgumentException>(() => TimerPalette.Get("unknown"));
    }
}
