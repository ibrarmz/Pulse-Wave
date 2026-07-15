using System.Text;
using Spectre.Console;

namespace Pulsewave.Rendering;

public sealed class ConsoleRenderer
{
    public void Prepare()
    {
        Console.CursorVisible = false;
        AnsiConsole.Console.Clear();
    }

    public void Render(BarCell[,] frame)
    {
        int rows = frame.GetLength(0);
        int columns = frame.GetLength(1);

        Console.SetCursorPosition(0, 0);
        var builder = new StringBuilder();

        for (int row = 0; row < rows; row++)
        {
            BarCell? current = null;
            var segment = new StringBuilder();

            for (int col = 0; col < columns; col++)
            {
                var cell = frame[row, col];
                if (current is null || cell.Color != current.Value.Color)
                {
                    if (current is not null)
                    {
                        AppendSegment(builder, current.Value.Color, segment.ToString());
                        segment.Clear();
                    }
                    current = cell;
                }
                segment.Append(cell.Glyph);
            }
            if (current is not null)
            {
                AppendSegment(builder, current.Value.Color, segment.ToString());
            }

            if (row < rows - 1)
            {
                builder.Append('\n');
            }
        }

        AnsiConsole.Markup(builder.ToString());
    }

    public void Restore()
    {
        Console.CursorVisible = true;
        AnsiConsole.Console.Clear();
    }

    private static void AppendSegment(StringBuilder builder, Color color, string text)
    {
        builder.Append($"[#{color.ToHex()}]{text.EscapeMarkup()}[/]");
    }
}
