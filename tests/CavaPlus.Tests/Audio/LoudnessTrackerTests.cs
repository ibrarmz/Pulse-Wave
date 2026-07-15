using CavaPlus.Audio;

namespace CavaPlus.Tests.Audio;

public class LoudnessTrackerTests
{
    [Fact]
    public void Update_WithSustainedLoudAudio_ConvergesNearMaximumBrightness()
    {
        var tracker = new LoudnessTracker();
        var loud = new float[256];
        Array.Fill(loud, 0.8f);

        float brightness = 0f;
        for (int i = 0; i < 30; i++)
        {
            brightness = tracker.Update(loud, loud);
        }

        Assert.True(brightness > 0.95f, $"Expected brightness near 1.0 after sustained loud audio, got {brightness}");
    }

    [Fact]
    public void Update_WithSilenceAfterLoudAudio_ConvergesNearMinimumBrightness()
    {
        var tracker = new LoudnessTracker();
        var loud = new float[256];
        Array.Fill(loud, 0.8f);
        var silence = new float[256];

        for (int i = 0; i < 30; i++)
        {
            tracker.Update(loud, loud);
        }

        float brightness = 0f;
        for (int i = 0; i < 30; i++)
        {
            brightness = tracker.Update(silence, silence);
        }

        Assert.True(brightness < 0.72f, $"Expected brightness near 0.7 after silence, got {brightness}");
    }

    [Fact]
    public void Update_ChangesGradually_NotInstantly()
    {
        var tracker = new LoudnessTracker();
        var loud = new float[256];
        Array.Fill(loud, 0.8f);

        float brightness = tracker.Update(loud, loud);

        Assert.InRange(brightness, 0.7f, 0.95f);
    }

    [Fact]
    public void Update_WithModerateLevelAfterLoudPeak_ProducesIntermediateBrightness()
    {
        var tracker = new LoudnessTracker();
        var loud = new float[256];
        Array.Fill(loud, 0.8f);
        var moderate = new float[256];
        Array.Fill(moderate, 0.4f); // half the established peak's amplitude

        for (int i = 0; i < 30; i++)
        {
            tracker.Update(loud, loud); // establishes peak at 0.8, brightness saturates near 1.0
        }

        float brightness = 0f;
        for (int i = 0; i < 10; i++)
        {
            // Peak decays slowly (0.995/frame), so 10 frames isn't enough for it to fully
            // catch down to the new 0.4 level - loudnessFraction should settle around ~0.5,
            // producing a brightness clearly between the post-loud saturation point and the
            // 0.7 floor, not pinned to either extreme.
            brightness = tracker.Update(moderate, moderate);
        }

        // A broken peak computation (backwards ratio, peak ignored, decay direction flipped)
        // would keep this pinned near 1.0 (still "loud" by whatever broken measure) or drop it
        // all the way to 0.7 (treating it as silence) instead of landing in between.
        Assert.InRange(brightness, 0.80f, 0.92f);
    }
}
