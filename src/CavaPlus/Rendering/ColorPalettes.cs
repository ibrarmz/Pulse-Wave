using Spectre.Console;

namespace CavaPlus.Rendering;

public static class ColorPalettes
{
    private static readonly Dictionary<string, string[]> Presets = new()
    {
        ["rainbow"] = new[] { "#FF0000", "#FF7F00", "#FFFF00", "#00FF00", "#0000FF", "#4B0082", "#9400D3" },
        ["neon"] = new[] { "#FF00FF", "#B84DFF", "#7B61FF", "#00D4FF" },
        ["fire"] = new[] { "#3D0000", "#B22200", "#FF6A00", "#FFD400" },
        ["ice"] = new[] { "#001A4D", "#0066CC", "#33CCFF", "#FFFFFF" },
        ["mono"] = new[] { "#003300", "#00AA00", "#00FF00", "#CCFFCC" },
    };

    public static IReadOnlyCollection<string> PresetNames => Presets.Keys;

    public static List<Color> Resolve(string paletteName, List<string> customGradientHex)
    {
        IEnumerable<string> hexColors = Presets.TryGetValue(paletteName, out var preset)
            ? preset
            : customGradientHex;

        return hexColors.Select(ParseHexColor).ToList();
    }

    public static Color ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(4, 2), 16);
        return new Color(r, g, b);
    }
}
