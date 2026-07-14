using NAudio.Wave;

namespace CavaPlus.Audio;

public sealed class AudioCapture : IDisposable
{
    private readonly WasapiLoopbackCapture _capture;
    private readonly SampleRingBuffer _buffer;

    public WaveFormat WaveFormat => _capture.WaveFormat;

    public AudioCapture(SampleRingBuffer buffer)
    {
        _buffer = buffer;
        _capture = new WasapiLoopbackCapture();

        if (_capture.WaveFormat.Channels != 2)
        {
            throw new NotSupportedException(
                $"CavaPlus solo soporta dispositivos de salida estéreo (2 canales); " +
                $"el dispositivo activo reporta {_capture.WaveFormat.Channels} canal(es).");
        }
        if (_capture.WaveFormat.BitsPerSample != 32 ||
            _capture.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
        {
            throw new NotSupportedException(
                "CavaPlus espera un formato de captura WASAPI IEEE float de 32 bits, " +
                $"pero el dispositivo reporta {_capture.WaveFormat.BitsPerSample} bits / {_capture.WaveFormat.Encoding}.");
        }

        _capture.DataAvailable += OnDataAvailable;
    }

    public void Start() => _capture.StartRecording();

    public void Stop() => _capture.StopRecording();

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        int bytesPerSample = _capture.WaveFormat.BitsPerSample / 8;
        int sampleCount = e.BytesRecorded / bytesPerSample;
        var samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            samples[i] = BitConverter.ToSingle(e.Buffer, i * bytesPerSample);
        }
        _buffer.WriteInterleavedStereo(samples);
    }

    public void Dispose()
    {
        _capture.DataAvailable -= OnDataAvailable;
        _capture.Dispose();
    }
}
