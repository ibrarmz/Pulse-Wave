using System.Linq;

namespace CavaPlus.Bars;

public sealed class BarMapper
{
    // Real WASAPI FFT magnitudes have no fixed loudness reference - their absolute scale depends
    // on system/app volume, which varies per machine and per moment. A fixed divisor either clips
    // everything to the max (too sensitive) or shows nothing (not sensitive enough) depending on
    // that volume. Instead, track a slowly-decaying recent peak and scale each bar relative to it,
    // so bar height reflects relative loudness regardless of the absolute magnitude scale.
    private const float PeakDecayPerFrame = 0.995f; // ~a few seconds to fully decay at 60fps
    private const float MinPeakMagnitude = 1e-3f;   // floor so scaling never divides by ~0 during silence

    // Real music's energy rolls off sharply above a few kHz, so an unweighted spectrum leaves
    // most of the upper bars looking permanently flat. FrequencyGainReferenceHz controls how much
    // extra gain higher-frequency bins get (gain = 1 + freqHz/reference) to compensate, and capping
    // the mapped range below the Nyquist/20kHz ceiling avoids spending bars on near-silent treble.
    private const float FrequencyGainReferenceHz = 6000f;
    private const float MaxMappedFrequencyHz = 12000f;

    private readonly int _sampleRate;
    private readonly int _fftLength;
    private readonly float _sensitivity;
    private readonly float _smoothingFactor;
    private readonly float _binWidthHz;
    private float[]? _previousHeights;
    private float _peakMagnitude = MinPeakMagnitude;

    public BarMapper(int sampleRate, int fftLength, float sensitivity, float smoothingFactor = 0.6f)
    {
        _sampleRate = sampleRate;
        _fftLength = fftLength;
        _sensitivity = sensitivity;
        _smoothingFactor = smoothingFactor;
        _binWidthHz = (float)sampleRate / fftLength;
    }

    public float[] MapToBars(float[] magnitudes, int barCount)
    {
        var edges = ComputeLogFrequencyEdges(barCount, magnitudes.Length);
        var barAverages = new float[barCount];

        for (int bar = 0; bar < barCount; bar++)
        {
            int startBin = edges[bar];
            int endBin = Math.Max(startBin + 1, edges[bar + 1]);
            float sum = 0f;
            int count = 0;
            for (int bin = startBin; bin < endBin && bin < magnitudes.Length; bin++)
            {
                float frequencyHz = bin * _binWidthHz;
                float gain = 1f + frequencyHz / FrequencyGainReferenceHz;
                sum += magnitudes[bin] * gain;
                count++;
            }

            barAverages[bar] = count > 0 ? sum / count : 0f;
        }

        float frameMax = barAverages.Length > 0 ? barAverages.Max() : 0f;
        _peakMagnitude = Math.Max(MinPeakMagnitude, Math.Max(frameMax, _peakMagnitude * PeakDecayPerFrame));

        var rawHeights = new float[barCount];
        for (int bar = 0; bar < barCount; bar++)
        {
            float scaled = barAverages[bar] / _peakMagnitude * (_sensitivity / 100f);
            rawHeights[bar] = Math.Clamp(scaled, 0f, 1f);
        }

        if (_previousHeights == null || _previousHeights.Length != barCount)
        {
            _previousHeights = new float[barCount];
        }

        var smoothed = new float[barCount];
        for (int bar = 0; bar < barCount; bar++)
        {
            smoothed[bar] = _previousHeights[bar] * _smoothingFactor + rawHeights[bar] * (1 - _smoothingFactor);
        }
        _previousHeights = smoothed;

        return smoothed;
    }

    private int[] ComputeLogFrequencyEdges(int barCount, int totalBins)
    {
        const float minFrequencyHz = 20f;
        float maxFrequencyHz = Math.Min(_sampleRate / 2f, MaxMappedFrequencyHz);
        float binWidthHz = (float)_sampleRate / _fftLength;

        var edges = new int[barCount + 1];
        for (int i = 0; i <= barCount; i++)
        {
            double t = (double)i / barCount;
            double frequencyHz = minFrequencyHz * Math.Pow(maxFrequencyHz / minFrequencyHz, t);
            int bin = (int)Math.Round(frequencyHz / binWidthHz);
            edges[i] = Math.Clamp(bin, 1, totalBins);
        }
        return edges;
    }
}
