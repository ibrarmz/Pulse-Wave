using CavaPlus.Spectrum;

namespace CavaPlus.Tests.Spectrum;

public class BassBeatDetectorTests
{
    // sampleRate=44100, fftLength=1024 -> bin width ~43.07 Hz -> bass cutoff (150 Hz) covers bins 0-2.
    private static float[] MakeMagnitudes(float bassBinValue)
    {
        var magnitudes = new float[512];
        magnitudes[0] = bassBinValue;
        magnitudes[1] = bassBinValue;
        magnitudes[2] = bassBinValue;
        return magnitudes;
    }

    [Fact]
    public void Update_OnFirstCall_InitializesBaselineWithoutTriggering()
    {
        var detector = new BassBeatDetector(sampleRate: 44100, fftLength: 1024);

        float flash = detector.Update(MakeMagnitudes(10f));

        Assert.Equal(0f, flash);
    }

    [Fact]
    public void Update_WithStableBass_NeverTriggers()
    {
        var detector = new BassBeatDetector(sampleRate: 44100, fftLength: 1024);
        detector.Update(MakeMagnitudes(10f)); // initializes baseline

        for (int i = 0; i < 5; i++)
        {
            float flash = detector.Update(MakeMagnitudes(10f));
            Assert.Equal(0f, flash);
        }
    }

    [Fact]
    public void Update_WithSuddenSpike_TriggersFullFlash()
    {
        var detector = new BassBeatDetector(sampleRate: 44100, fftLength: 1024);
        detector.Update(MakeMagnitudes(10f)); // initializes baseline
        detector.Update(MakeMagnitudes(10f)); // confirms flat baseline, no trigger yet

        float flash = detector.Update(MakeMagnitudes(50f)); // well above baseline * 1.6

        Assert.Equal(1f, flash);
    }

    [Fact]
    public void Update_AfterSpike_FlashDecaysOverSubsequentFrames()
    {
        var detector = new BassBeatDetector(sampleRate: 44100, fftLength: 1024);
        detector.Update(MakeMagnitudes(10f));
        detector.Update(MakeMagnitudes(10f));
        detector.Update(MakeMagnitudes(50f)); // triggers, flash = 1.0

        float firstDecay = detector.Update(MakeMagnitudes(10f));
        float secondDecay = detector.Update(MakeMagnitudes(10f));

        Assert.Equal(0.5f, firstDecay, precision: 3);
        Assert.Equal(0.25f, secondDecay, precision: 3);
    }

    [Fact]
    public void Update_WithSpikeBelowMinimumFloor_DoesNotTrigger()
    {
        var detector = new BassBeatDetector(sampleRate: 44100, fftLength: 1024);
        detector.Update(MakeMagnitudes(0.0001f)); // initializes a very quiet baseline

        // 0.0005 clears the ratio threshold (0.0001 * 1.6 = 0.00016) but stays under the
        // absolute minimum floor (0.001), so near-total silence should never "flash".
        float flash = detector.Update(MakeMagnitudes(0.0005f));

        Assert.Equal(0f, flash);
    }

    [Fact]
    public void Update_WithSecondSpikeDuringCooldown_DoesNotReTrigger()
    {
        var detector = new BassBeatDetector(sampleRate: 44100, fftLength: 1024);
        detector.Update(MakeMagnitudes(10f)); // initializes baseline
        detector.Update(MakeMagnitudes(10f)); // confirms flat baseline
        detector.Update(MakeMagnitudes(50f)); // triggers, flash = 1.0, cooldown starts

        // A second loud spike arriving while still in cooldown must NOT re-trigger a fresh
        // 1.0 - it should just decay from the prior flash like any other non-triggering frame.
        float flash = detector.Update(MakeMagnitudes(80f));

        Assert.Equal(0.5f, flash, precision: 3);
    }
}
