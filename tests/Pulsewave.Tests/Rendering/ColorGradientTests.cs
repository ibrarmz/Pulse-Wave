using Pulsewave.Rendering;
using Spectre.Console;

namespace Pulsewave.Tests.Rendering;

public class ColorGradientTests
{
    private static readonly List<Color> Stops = new()
    {
        new Color(0, 255, 0),
        new Color(255, 255, 0),
        new Color(255, 0, 0),
    };

    [Fact]
    public void Interpolate_AtZero_ReturnsFirstStop()
    {
        var result = ColorGradient.Interpolate(Stops, 0f);
        Assert.Equal(new Color(0, 255, 0), result);
    }

    [Fact]
    public void Interpolate_AtOne_ReturnsLastStop()
    {
        var result = ColorGradient.Interpolate(Stops, 1f);
        Assert.Equal(new Color(255, 0, 0), result);
    }

    [Fact]
    public void Interpolate_AtMidpoint_ReturnsMiddleStop()
    {
        var result = ColorGradient.Interpolate(Stops, 0.5f);
        Assert.Equal(new Color(255, 255, 0), result);
    }

    [Fact]
    public void Interpolate_BetweenFirstTwoStops_BlendsLinearly()
    {
        var twoStops = new List<Color> { new Color(0, 0, 0), new Color(100, 100, 100) };
        var result = ColorGradient.Interpolate(twoStops, 0.5f);
        Assert.Equal(new Color(50, 50, 50), result);
    }
}
