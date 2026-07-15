using Pulsewave.Rendering;
using Spectre.Console;

namespace Pulsewave.Tests.Rendering;

public class OverlayTextTests
{
    [Fact]
    public void Apply_CentersShortTextWithinFrame()
    {
        var frame = new BarCell[1, 10];
        for (int col = 0; col < 10; col++)
        {
            frame[0, col] = new BarCell('█', Color.Green);
        }

        OverlayText.Apply(frame, "hi", row: 0, color: Color.White);

        Assert.Equal(' ', frame[0, 0].Glyph);
        Assert.Equal(' ', frame[0, 3].Glyph);
        Assert.Equal('h', frame[0, 4].Glyph);
        Assert.Equal('i', frame[0, 5].Glyph);
        Assert.Equal(' ', frame[0, 6].Glyph);
        Assert.Equal(' ', frame[0, 9].Glyph);
        Assert.Equal(Color.White, frame[0, 0].Color);
        Assert.Equal(Color.White, frame[0, 4].Color);
    }

    [Fact]
    public void Apply_TruncatesTextLongerThanFrame_WithEllipsis()
    {
        var frame = new BarCell[1, 5];

        OverlayText.Apply(frame, "abcdefgh", row: 0, color: Color.White);

        Assert.Equal("abcd…", new string(new[]
        {
            frame[0, 0].Glyph, frame[0, 1].Glyph, frame[0, 2].Glyph, frame[0, 3].Glyph, frame[0, 4].Glyph,
        }));
    }

    [Fact]
    public void Apply_WithRowOutOfRange_LeavesFrameUnchanged()
    {
        var frame = new BarCell[2, 4];
        frame[0, 0] = new BarCell('█', Color.Green);
        frame[1, 2] = new BarCell('▄', Color.Red);

        OverlayText.Apply(frame, "hi", row: 5, color: Color.White);
        OverlayText.Apply(frame, "hi", row: -1, color: Color.White);

        Assert.Equal('█', frame[0, 0].Glyph);
        Assert.Equal('▄', frame[1, 2].Glyph);
    }

    [Fact]
    public void Apply_WithTextExactlyFillingFrame_ShowsFullText()
    {
        var frame = new BarCell[1, 5];

        OverlayText.Apply(frame, "hello", row: 0, color: Color.White);

        Assert.Equal("hello", new string(new[]
        {
            frame[0, 0].Glyph, frame[0, 1].Glyph, frame[0, 2].Glyph, frame[0, 3].Glyph, frame[0, 4].Glyph,
        }));
    }
}
