using CavaPlus.Rendering;
using Spectre.Console;

namespace CavaPlus.Tests.Rendering;

public class ColorPalettesTests
{
    [Fact]
    public void Resolve_WithRainbowPreset_ReturnsExpectedColors()
    {
        var colors = ColorPalettes.Resolve("rainbow", customGradientHex: new List<string>());

        Assert.Equal(7, colors.Count);
        Assert.Equal(new Color(0xFF, 0x00, 0x00), colors[0]);
        Assert.Equal(new Color(0x94, 0x00, 0xD3), colors[^1]);
    }

    [Fact]
    public void Resolve_WithFirePreset_ReturnsExpectedColors()
    {
        var colors = ColorPalettes.Resolve("fire", customGradientHex: new List<string>());

        Assert.Equal(4, colors.Count);
        Assert.Equal(new Color(0x3D, 0x00, 0x00), colors[0]);
        Assert.Equal(new Color(0xFF, 0xD4, 0x00), colors[^1]);
    }

    [Fact]
    public void Resolve_WithNeonAndIcePresets_ReturnEndpointsMatchingSpec()
    {
        var neon = ColorPalettes.Resolve("neon", customGradientHex: new List<string>());
        var ice = ColorPalettes.Resolve("ice", customGradientHex: new List<string>());

        Assert.Equal(new Color(0xFF, 0x00, 0xFF), neon[0]);
        Assert.Equal(new Color(0x00, 0xD4, 0xFF), neon[^1]);
        Assert.Equal(new Color(0x00, 0x1A, 0x4D), ice[0]);
        Assert.Equal(new Color(0xFF, 0xFF, 0xFF), ice[^1]);
    }

    [Fact]
    public void Resolve_WithMonoPreset_ReturnsExpectedColors()
    {
        var colors = ColorPalettes.Resolve("mono", customGradientHex: new List<string>());

        Assert.Equal(new Color(0x00, 0x33, 0x00), colors[0]);
        Assert.Equal(new Color(0xCC, 0xFF, 0xCC), colors[^1]);
    }

    [Fact]
    public void Resolve_WithCustom_ParsesProvidedGradient()
    {
        var colors = ColorPalettes.Resolve("custom", customGradientHex: new List<string> { "#112233", "#445566" });

        Assert.Equal(2, colors.Count);
        Assert.Equal(new Color(0x11, 0x22, 0x33), colors[0]);
        Assert.Equal(new Color(0x44, 0x55, 0x66), colors[1]);
    }

    [Fact]
    public void ParseHexColor_ParsesSixDigitHex()
    {
        var color = ColorPalettes.ParseHexColor("#00FF7F");

        Assert.Equal(new Color(0x00, 0xFF, 0x7F), color);
    }
}
