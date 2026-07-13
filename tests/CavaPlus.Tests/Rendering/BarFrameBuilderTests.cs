using CavaPlus.Rendering;
using Spectre.Console;

namespace CavaPlus.Tests.Rendering;

public class BarFrameBuilderTests
{
    [Fact]
    public void BuildFrame_ClassicStyle_FullHeightBarFillsAllRowsWithSolidBlock()
    {
        var builder = new BarFrameBuilder(BarStyle.Classic, barWidth: 1, gap: 0,
            colorStops: new List<Color> { Color.Green }, solidColor: true);

        var frame = builder.BuildFrame(new float[] { 1f }, terminalHeightRows: 4);

        for (int row = 0; row < 4; row++)
        {
            Assert.Equal('█', frame[row, 0].Glyph);
        }
    }

    [Fact]
    public void BuildFrame_ClassicStyle_HalfHeightBarFillsBottomRowsOnly()
    {
        var builder = new BarFrameBuilder(BarStyle.Classic, barWidth: 1, gap: 0,
            colorStops: new List<Color> { Color.Green }, solidColor: true);

        var frame = builder.BuildFrame(new float[] { 0.5f }, terminalHeightRows: 4);

        Assert.Equal(' ', frame[0, 0].Glyph);
        Assert.Equal(' ', frame[1, 0].Glyph);
        Assert.Equal('█', frame[2, 0].Glyph);
        Assert.Equal('█', frame[3, 0].Glyph);
    }

    [Fact]
    public void BuildFrame_GradientStyle_ZeroHeightBarIsBlank()
    {
        var builder = new BarFrameBuilder(BarStyle.Gradient, barWidth: 1, gap: 0,
            colorStops: new List<Color> { Color.Green }, solidColor: true);

        var frame = builder.BuildFrame(new float[] { 0f }, terminalHeightRows: 4);

        for (int row = 0; row < 4; row++)
        {
            Assert.Equal(' ', frame[row, 0].Glyph);
        }
    }

    [Fact]
    public void BuildFrame_GradientStyle_PartialHeightProducesSubCharacterGlyph()
    {
        var builder = new BarFrameBuilder(BarStyle.Gradient, barWidth: 1, gap: 0,
            colorStops: new List<Color> { Color.Green }, solidColor: true);

        var frame = builder.BuildFrame(new float[] { 0.5f }, terminalHeightRows: 1);

        Assert.Equal('▄', frame[0, 0].Glyph);
    }

    [Fact]
    public void BuildFrame_WithGap_LeavesBlankColumnsBetweenBars()
    {
        var builder = new BarFrameBuilder(BarStyle.Classic, barWidth: 1, gap: 1,
            colorStops: new List<Color> { Color.Green }, solidColor: true);

        var frame = builder.BuildFrame(new float[] { 1f, 1f }, terminalHeightRows: 1);

        Assert.Equal('█', frame[0, 0].Glyph); // bar 0
        Assert.Equal(' ', frame[0, 1].Glyph); // gap
        Assert.Equal('█', frame[0, 2].Glyph); // bar 1
    }
}
