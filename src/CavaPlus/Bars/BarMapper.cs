namespace CavaPlus.Bars;

public sealed class BarMapper
{
    // Heuristic scale between raw FFT magnitude and a 0..1 bar height: NAudio's magnitudes
    // aren't calibrated to a fixed loudness reference, so this needs re-tuning by ear once
    // real WASAPI audio is wired in (later task).
    private const float MagnitudeNormalization = 50f;

    private readonly int _sampleRate;
    private readonly int _fftLength;
    private readonly float _sensitivity;
    private readonly float _smoothingFactor;
    private float[]? _previousHeights;

    public BarMapper(int sampleRate, int fftLength, float sensitivity, float smoothingFactor = 0.6f)
    {
        _sampleRate = sampleRate;
        _fftLength = fftLength;
        _sensitivity = sensitivity;
        _smoothingFactor = smoothingFactor;
    }

    public float[] MapToBars(float[] magnitudes, int barCount)
    {
        var edges = ComputeLogFrequencyEdges(barCount, magnitudes.Length);
        var rawHeights = new float[barCount];

        for (int bar = 0; bar < barCount; bar++)
        {
            int startBin = edges[bar];
            int endBin = Math.Max(startBin + 1, edges[bar + 1]);
            float sum = 0f;
            int count = 0;
            for (int bin = startBin; bin < endBin && bin < magnitudes.Length; bin++)
            {
                sum += magnitudes[bin];
                count++;
            }

            float average = count > 0 ? sum / count : 0f;
            float scaled = average * _sensitivity / 100f / MagnitudeNormalization;
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
        float maxFrequencyHz = Math.Min(_sampleRate / 2f, 20000f);
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
