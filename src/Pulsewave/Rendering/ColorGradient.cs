using Spectre.Console;

namespace Pulsewave.Rendering;

public static class ColorGradient
{
    public static Color Interpolate(IReadOnlyList<Color> stops, float t)
    {
        if (stops.Count == 0)
        {
            throw new ArgumentException("At least one color stop is required", nameof(stops));
        }
        if (stops.Count == 1)
        {
            return stops[0];
        }

        t = Math.Clamp(t, 0f, 1f);
        float scaled = t * (stops.Count - 1);
        int index = (int)MathF.Floor(scaled);
        if (index >= stops.Count - 1)
        {
            return stops[^1];
        }

        float localT = scaled - index;
        var a = stops[index];
        var b = stops[index + 1];

        byte r = (byte)(a.R + (b.R - a.R) * localT);
        byte g = (byte)(a.G + (b.G - a.G) * localT);
        byte bl = (byte)(a.B + (b.B - a.B) * localT);
        return new Color(r, g, bl);
    }
}
