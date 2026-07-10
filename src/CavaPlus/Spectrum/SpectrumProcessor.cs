using NAudio.Dsp;

namespace CavaPlus.Spectrum;

public sealed class SpectrumProcessor
{
    private readonly int _fftLength;
    private readonly int _m;
    private readonly float[] _window;

    public SpectrumProcessor(int fftLength)
    {
        if (fftLength <= 0 || (fftLength & (fftLength - 1)) != 0)
        {
            throw new ArgumentException("fftLength must be a power of two", nameof(fftLength));
        }

        _fftLength = fftLength;
        _m = (int)Math.Log2(fftLength);
        _window = HannWindow(fftLength);
    }

    public float[] ComputeMagnitudes(float[] samples)
    {
        if (samples.Length != _fftLength)
        {
            throw new ArgumentException($"Expected {_fftLength} samples, got {samples.Length}", nameof(samples));
        }

        var complex = new Complex[_fftLength];
        for (int i = 0; i < _fftLength; i++)
        {
            complex[i].X = samples[i] * _window[i];
            complex[i].Y = 0;
        }

        FastFourierTransform.FFT(true, _m, complex);

        var magnitudes = new float[_fftLength / 2];
        for (int i = 0; i < magnitudes.Length; i++)
        {
            magnitudes[i] = MathF.Sqrt(complex[i].X * complex[i].X + complex[i].Y * complex[i].Y);
        }

        return magnitudes;
    }

    private static float[] HannWindow(int length)
    {
        var window = new float[length];
        for (int i = 0; i < length; i++)
        {
            window[i] = 0.5f * (1 - MathF.Cos(2 * MathF.PI * i / (length - 1)));
        }
        return window;
    }
}
