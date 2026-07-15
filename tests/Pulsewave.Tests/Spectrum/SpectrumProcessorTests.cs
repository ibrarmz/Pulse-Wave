using Pulsewave.Spectrum;

namespace Pulsewave.Tests.Spectrum;

public class SpectrumProcessorTests
{
    [Fact]
    public void ComputeMagnitudes_PureSineWave_PeaksAtExpectedBin()
    {
        const int sampleRate = 44100;
        const int fftLength = 1024;
        const float frequencyHz = 440f;

        var samples = new float[fftLength];
        for (int i = 0; i < fftLength; i++)
        {
            samples[i] = MathF.Sin(2 * MathF.PI * frequencyHz * i / sampleRate);
        }

        var processor = new SpectrumProcessor(fftLength);
        var magnitudes = processor.ComputeMagnitudes(samples);

        int expectedBin = (int)MathF.Round(frequencyHz * fftLength / sampleRate);

        int peakBin = 1;
        for (int i = 2; i < magnitudes.Length; i++)
        {
            if (magnitudes[i] > magnitudes[peakBin]) peakBin = i;
        }

        Assert.InRange(peakBin, expectedBin - 1, expectedBin + 1);
    }

    [Fact]
    public void Constructor_WithNonPowerOfTwoLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => new SpectrumProcessor(1000));
    }

    [Fact]
    public void ComputeMagnitudes_WithWrongSampleLength_Throws()
    {
        var processor = new SpectrumProcessor(1024);
        Assert.Throws<ArgumentException>(() => processor.ComputeMagnitudes(new float[500]));
    }
}
