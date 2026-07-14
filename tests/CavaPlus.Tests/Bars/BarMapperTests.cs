using CavaPlus.Bars;

namespace CavaPlus.Tests.Bars;

public class BarMapperTests
{
    [Fact]
    public void MapToBars_ClampsHeightsToUnitRange()
    {
        var mapper = new BarMapper(sampleRate: 44100, fftLength: 1024, sensitivity: 500f, smoothingFactor: 0f);
        var magnitudes = new float[512];
        Array.Fill(magnitudes, 1000f);

        var heights = mapper.MapToBars(magnitudes, barCount: 4);

        Assert.All(heights, h => Assert.InRange(h, 0f, 1f));
    }

    [Fact]
    public void MapToBars_SmoothsBetweenConsecutiveFrames()
    {
        var mapper = new BarMapper(sampleRate: 44100, fftLength: 1024, sensitivity: 100f, smoothingFactor: 0.6f);
        var silence = new float[512];
        var loud = new float[512];
        Array.Fill(loud, 1000f);

        mapper.MapToBars(silence, barCount: 8); // establishes near-zero previous heights

        var secondFrame = mapper.MapToBars(loud, barCount: 8);

        foreach (var height in secondFrame)
        {
            Assert.True(height < 1.0f, "Smoothing should prevent an instant jump to the clamped max");
            Assert.True(height > 0f);
        }
    }

    [Fact]
    public void MapToBars_LowFrequencyEnergyMapsToEarlyBars()
    {
        var mapper = new BarMapper(sampleRate: 44100, fftLength: 1024, sensitivity: 100f, smoothingFactor: 0f);
        var magnitudes = new float[512];
        magnitudes[2] = 1000f; // very low-frequency bin (~86 Hz at this sample rate/fft length)

        var heights = mapper.MapToBars(magnitudes, barCount: 8);

        float lowHalfMax = heights.Take(4).Max();
        float highHalfMax = heights.Skip(4).Max();
        Assert.True(lowHalfMax > highHalfMax);
    }

    [Fact]
    public void MapToBars_ScalesRelativeToRecentPeak_NotAbsoluteMagnitude()
    {
        // Regression test: a fixed linear divisor clamped every real-world magnitude to 1.0
        // regardless of relative loudness (observed as a frozen, fully-lit display against real
        // audio). Scaling relative to a tracked recent peak should instead produce a height
        // proportional to how loud "quieter" is compared to "loud", not pinned to the clamp.
        var mapper = new BarMapper(sampleRate: 44100, fftLength: 1024, sensitivity: 100f, smoothingFactor: 0f);
        var loud = new float[512];
        Array.Fill(loud, 1000f);
        var halfAsLoud = new float[512];
        Array.Fill(halfAsLoud, 500f);

        mapper.MapToBars(loud, barCount: 4); // establishes a recent peak around 1000

        var heights = mapper.MapToBars(halfAsLoud, barCount: 4);

        Assert.All(heights, h => Assert.InRange(h, 0.4f, 0.6f));
    }
}
