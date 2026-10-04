namespace GaleWorkTimer.Domain;

public sealed record TimerColor(string ColorKey, string Name, string Light, string Dark);

public static class TimerPalette
{
    public const string DefaultKey = "blue";
    public static IReadOnlyList<TimerColor> Colors { get; } = Array.AsReadOnly(new[]
    {
        new TimerColor("blue", "Blue", "#245EA8", "#8AB4E8"),
        new TimerColor("purple", "Purple", "#7046A3", "#BDA0DF"),
        new TimerColor("cyan", "Cyan", "#00758A", "#73C9DC"),
        new TimerColor("teal", "Teal", "#176F68", "#7FC9BC"),
        new TimerColor("green", "Green", "#3F702D", "#A3C985"),
        new TimerColor("yellow", "Yellow", "#846500", "#D9C36A"),
        new TimerColor("orange", "Orange", "#A65314", "#E8AD78"),
        new TimerColor("red", "Red", "#AD3D39", "#EAA09A"),
        new TimerColor("pink", "Pink", "#A13E74", "#DEA0C2"),
        new TimerColor("gray", "Gray", "#59636D", "#ADB6BF")
    });

    public static TimerColor Get(string colorKey) => Colors.FirstOrDefault(color => color.ColorKey == colorKey)
        ?? throw new ArgumentException("Choose a color from the timer palette.", nameof(colorKey));

    public static string NextKey(int activeCount) => Colors[activeCount % Colors.Count].ColorKey;
}
