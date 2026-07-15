using CavaPlus.Rendering;
using Spectre.Console;

namespace CavaPlus.Tests.Rendering;

public class ColorReactivityTests
{
    [Fact]
    public void Apply_WithFullBrightness_LeavesFrameUnchanged()
    {
        var frame = new BarCell[1, 1];
        frame[0, 0] = new BarCell('█', new Color(100, 150, 200));

        ColorReactivity.Apply(frame, brightnessFactor: 1.0f);

        Assert.Equal(new Color(100, 150, 200), frame[0, 0].Color);
        Assert.Equal('█', frame[0, 0].Glyph);
    }

    [Fact]
    public void Apply_WithReducedBrightness_ScalesEachChannel()
    {
        var frame = new BarCell[1, 1];
        frame[0, 0] = new BarCell('█', new Color(200, 100, 50));

        ColorReactivity.Apply(frame, brightnessFactor: 0.5f);

        Assert.Equal(new Color(100, 50, 25), frame[0, 0].Color);
    }

    [Fact]
    public void Apply_PreservesGlyphs()
    {
        var frame = new BarCell[1, 2];
        frame[0, 0] = new BarCell('▄', new Color(10, 20, 30));
        frame[0, 1] = new BarCell(' ', new Color(0, 0, 0));

        ColorReactivity.Apply(frame, brightnessFactor: 0.8f);

        Assert.Equal('▄', frame[0, 0].Glyph);
        Assert.Equal(' ', frame[0, 1].Glyph);
    }
}
