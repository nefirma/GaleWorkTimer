using System.Xml.Linq;
using Avalonia.Media;
using Avalonia.Platform;

namespace GaleWorkTimer.Views;

public static class CopyIcon
{
    public static Geometry Geometry { get; } = LoadGeometry();

    private static Geometry LoadGeometry()
    {
        using var svg = AssetLoader.Open(new Uri("avares://GaleWorkTimer/Assets/Icons/copy_20_regular.svg"));
        var path = XDocument.Load(svg).Root!.Element(XName.Get("path", "http://www.w3.org/2000/svg"))!;
        // Use the official SVG path verbatim, with SVG's default nonzero fill rule.
        return StreamGeometry.Parse("F1 " + path.Attribute("d")!.Value);
    }
}
