using Spectre.Console;

namespace Pulsewave.Rendering;

public enum BarStyle
{
    Classic,
    Gradient,
}

public readonly record struct BarCell(char Glyph, Color Color);

public sealed class BarFrameBuilder
{
    private static readonly char[] SubCharacterLevels = { ' ', '▁', '▂', '▃', '▄', '▅', '▆', '▇', '█' };

    private readonly BarStyle _style;
    private readonly int _barWidth;
    private readonly int _gap;
    private readonly IReadOnlyList<Color> _colorStops;
    private readonly bool _solidColor;

    public BarFrameBuilder(BarStyle style, int barWidth, int gap, IReadOnlyList<Color> colorStops, bool solidColor)
    {
        _style = style;
        _barWidth = barWidth;
        _gap = gap;
        _colorStops = colorStops;
        _solidColor = solidColor;
    }

    public BarCell[,] BuildFrame(float[] barHeights, int terminalHeightRows)
    {
        int columns = barHeights.Length * (_barWidth + _gap);
        var frame = new BarCell[terminalHeightRows, columns];

        for (int bar = 0; bar < barHeights.Length; bar++)
        {
            float height = Math.Clamp(barHeights[bar], 0f, 1f);
            int baseColumn = bar * (_barWidth + _gap);

            for (int row = 0; row < terminalHeightRows; row++)
            {
                float rowBottomFraction = 1f - (float)row / terminalHeightRows;
                char glyph = ResolveGlyph(height, row, terminalHeightRows);
                Color color = _solidColor ? _colorStops[0] : ColorGradient.Interpolate(_colorStops, rowBottomFraction);

                for (int col = 0; col < _barWidth; col++)
                {
                    frame[row, baseColumn + col] = new BarCell(glyph, color);
                }
                for (int col = _barWidth; col < _barWidth + _gap && baseColumn + col < columns; col++)
                {
                    frame[row, baseColumn + col] = new BarCell(' ', color);
                }
            }
        }

        return frame;
    }

    private char ResolveGlyph(float height, int rowFromTop, int totalRows)
    {
        float filledRows = height * totalRows;
        int rowFromBottom = totalRows - 1 - rowFromTop;

        if (_style == BarStyle.Classic)
        {
            return rowFromBottom < filledRows ? '█' : ' ';
        }

        float remaining = filledRows - rowFromBottom;
        if (remaining <= 0) return ' ';
        if (remaining >= 1) return '█';

        int levelIndex = (int)MathF.Round(remaining * (SubCharacterLevels.Length - 1));
        return SubCharacterLevels[Math.Clamp(levelIndex, 0, SubCharacterLevels.Length - 1)];
    }
}
