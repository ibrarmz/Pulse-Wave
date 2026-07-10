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
}
