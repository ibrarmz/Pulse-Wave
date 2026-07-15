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
}
