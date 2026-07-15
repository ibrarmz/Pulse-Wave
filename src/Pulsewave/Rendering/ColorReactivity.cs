using Spectre.Console;

namespace Pulsewave.Rendering;

public static class ColorReactivity
{
    public static void Apply(BarCell[,] frame, float brightnessFactor)
    {
        int rows = frame.GetLength(0);
        int columns = frame.GetLength(1);

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                var cell = frame[row, col];
                byte r = ScaleChannel(cell.Color.R, brightnessFactor);
                byte g = ScaleChannel(cell.Color.G, brightnessFactor);
                byte b = ScaleChannel(cell.Color.B, brightnessFactor);
                frame[row, col] = new BarCell(cell.Glyph, new Color(r, g, b));
            }
        }
    }

    private static byte ScaleChannel(byte value, float brightnessFactor)
    {
        float scaled = value * brightnessFactor;
        return (byte)Math.Clamp(scaled, 0f, 255f);
    }
}
