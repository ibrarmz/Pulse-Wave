using Spectre.Console;

namespace Pulsewave.Rendering;

public static class OverlayText
{
    public static void Apply(BarCell[,] frame, string text, int row, Color color)
    {
        int rows = frame.GetLength(0);
        int columns = frame.GetLength(1);

        if (row < 0 || row >= rows)
        {
            return;
        }

        string display = Truncate(text, columns);
        int startColumn = Math.Max(0, (columns - display.Length) / 2);

        for (int col = 0; col < columns; col++)
        {
            int textIndex = col - startColumn;
            char glyph = textIndex >= 0 && textIndex < display.Length ? display[textIndex] : ' ';
            frame[row, col] = new BarCell(glyph, color);
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        if (maxLength <= 0)
        {
            return string.Empty;
        }
        if (text.Length <= maxLength)
        {
            return text;
        }
        if (maxLength == 1)
        {
            return "…";
        }
        return text[..(maxLength - 1)] + "…";
    }
}
