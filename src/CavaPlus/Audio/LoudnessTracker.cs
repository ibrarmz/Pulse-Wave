namespace CavaPlus.Audio;

public sealed class LoudnessTracker
{
    // Same relative-peak trick as BarMapper: raw RMS has no fixed loudness reference (it depends
    // on system/app volume), so brightness is scaled against a slowly-decaying recent peak instead
    // of an absolute threshold.
    private const float PeakDecayPerFrame = 0.995f;
    private const float MinPeak = 1e-4f;
    private const float MinBrightness = 0.7f;
    private const float MaxBrightness = 1.0f;
    private const float SmoothingFactor = 0.15f;

    private float _peak = MinPeak;
    private float _smoothedBrightness = MinBrightness;

    public float Update(float[] leftSamples, float[] rightSamples)
    {
        float sumSquares = 0f;
        foreach (float sample in leftSamples)
        {
            sumSquares += sample * sample;
        }
        foreach (float sample in rightSamples)
        {
            sumSquares += sample * sample;
        }

        int count = leftSamples.Length + rightSamples.Length;
        float rms = count > 0 ? MathF.Sqrt(sumSquares / count) : 0f;

        _peak = Math.Max(MinPeak, Math.Max(rms, _peak * PeakDecayPerFrame));
        float loudnessFraction = Math.Clamp(rms / _peak, 0f, 1f);
        float target = MinBrightness + (MaxBrightness - MinBrightness) * loudnessFraction;

        _smoothedBrightness += (target - _smoothedBrightness) * SmoothingFactor;
        return _smoothedBrightness;
    }
}
