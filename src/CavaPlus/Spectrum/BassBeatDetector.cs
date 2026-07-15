namespace CavaPlus.Spectrum;

public sealed class BassBeatDetector
{
    // Frequencies below this rough cutoff (kick/bass energy) drive beat detection; higher
    // frequencies are ignored so hi-hats/vocals don't trigger false flashes.
    private const float BassCutoffHz = 150f;
    private const float BaselineSmoothingFactor = 0.05f;
    private const float TriggerRatio = 1.6f;
    private const float MinimumBassFloor = 1e-3f;
    private const int CooldownFrames = 6;
    private const float DecayRate = 0.5f;

    private readonly int _bassBinCount;
    private bool _initialized;
    private float _baseline;
    private float _flashIntensity;
    private int _cooldownRemaining;

    public BassBeatDetector(int sampleRate, int fftLength)
    {
        float binWidthHz = (float)sampleRate / fftLength;
        _bassBinCount = Math.Max(1, (int)(BassCutoffHz / binWidthHz));
    }

    public float Update(float[] magnitudes)
    {
        float bassEnergy = ComputeBassEnergy(magnitudes);

        // First call has no history to compare against - treat it as establishing the baseline
        // rather than evaluating a trigger, otherwise the very first frame of any audio would
        // look like a "spike" against a baseline of zero.
        if (!_initialized)
        {
            _baseline = bassEnergy;
            _initialized = true;
            return _flashIntensity;
        }

        bool triggered = _cooldownRemaining == 0
            && bassEnergy > MinimumBassFloor
            && bassEnergy > _baseline * TriggerRatio;

        _baseline += (bassEnergy - _baseline) * BaselineSmoothingFactor;

        if (triggered)
        {
            _flashIntensity = 1f;
            _cooldownRemaining = CooldownFrames;
        }
        else
        {
            _flashIntensity *= DecayRate;
            if (_cooldownRemaining > 0)
            {
                _cooldownRemaining--;
            }
        }

        return _flashIntensity;
    }

    private float ComputeBassEnergy(float[] magnitudes)
    {
        int bins = Math.Min(_bassBinCount, magnitudes.Length);
        float sum = 0f;
        for (int i = 0; i < bins; i++)
        {
            sum += magnitudes[i];
        }
        return bins > 0 ? sum / bins : 0f;
    }
}
