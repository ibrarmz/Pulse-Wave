using Spectre.Console;

namespace CavaPlus.Rendering;

public static class ColorReactivity
{
    // At flashIntensity == 1.0, cells mix 60% toward white - a visible flash, not a whiteout.
    private const float FlashWhiteMixStrength = 0.6f;

    public static void Apply(BarCell[,] frame, float brightnessFactor, float flashIntensity)
    {
        int rows = frame.GetLength(0);
        int columns = frame.GetLength(1);
        float whiteMix = Math.Clamp(flashIntensity, 0f, 1f) * FlashWhiteMixStrength;

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                var cell = frame[row, col];
                byte r = ScaleChannel(cell.Color.R, brightnessFactor, whiteMix);
                byte g = ScaleChannel(cell.Color.G, brightnessFactor, whiteMix);
                byte b = ScaleChannel(cell.Color.B, brightnessFactor, whiteMix);
                frame[row, col] = new BarCell(cell.Glyph, new Color(r, g, b));
            }
        }
    }

    private static byte ScaleChannel(byte value, float brightnessFactor, float whiteMix)
    {
        float scaled = value * brightnessFactor;
        float mixed = scaled + (255f - scaled) * whiteMix;
        return (byte)Math.Clamp(mixed, 0f, 255f);
    }
}
