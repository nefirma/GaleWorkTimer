namespace GaleWorkTimer.Domain;

public sealed record TimerColor(string ColorKey, string Name, string Light, string Dark)
{
    public string LightForeground => ContrastingText(Light);
    public string DarkForeground => ContrastingText(Dark);

    private static string ContrastingText(string hex)
    {
        static double Linear(string channel)
        {
            var value = Convert.ToInt32(channel, 16) / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        var luminance = 0.2126 * Linear(hex.Substring(1, 2)) + 0.7152 * Linear(hex.Substring(3, 2))
            + 0.0722 * Linear(hex.Substring(5, 2));
        return (luminance + 0.05) / 0.05 >= 1.05 / (luminance + 0.05) ? "#000000" : "#FFFFFF";
    }
}

public static class TimerPalette
{
    public const string DefaultKey = "blue";
    public static IReadOnlyList<TimerColor> Colors { get; } = Array.AsReadOnly(new[]
    {
        new TimerColor("blue", "Blue", "#1565C0", "#1976D2"),
        new TimerColor("purple", "Purple", "#6A1B9A", "#8E24AA"),
        new TimerColor("cyan", "Cyan", "#00BCD4", "#00ACC1"),
        new TimerColor("teal", "Teal", "#00695C", "#00796B"),
        new TimerColor("green", "Green", "#64DD17", "#76FF03"),
        new TimerColor("yellow", "Yellow", "#FFD600", "#FFEA00"),
        new TimerColor("orange", "Orange", "#FF6D00", "#FF9100"),
        new TimerColor("red", "Red", "#D50000", "#D32F2F"),
        new TimerColor("pink", "Magenta", "#C51162", "#D81B60"),
        new TimerColor("gray", "Gray", "#607D8B", "#78909C")
    });

    public static TimerColor Get(string colorKey) => Colors.FirstOrDefault(color => color.ColorKey == colorKey)
        ?? throw new ArgumentException("Choose a color from the timer palette.", nameof(colorKey));

    public static string NextKey(int activeCount) => Colors[activeCount % Colors.Count].ColorKey;
}
