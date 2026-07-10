namespace CavaPlus.Audio;

public sealed class SampleRingBuffer
{
    private readonly float[] _left;
    private readonly float[] _right;
    private readonly int _capacity;
    private readonly object _lock = new();
    private int _writePosition;
    private int _samplesWritten;

    public SampleRingBuffer(int capacityPerChannel)
    {
        _capacity = capacityPerChannel;
        _left = new float[capacityPerChannel];
        _right = new float[capacityPerChannel];
    }

    public void WriteInterleavedStereo(float[] interleaved)
    {
        lock (_lock)
        {
            int frames = interleaved.Length / 2;
            for (int i = 0; i < frames; i++)
            {
                _left[_writePosition] = interleaved[i * 2];
                _right[_writePosition] = interleaved[i * 2 + 1];
                _writePosition = (_writePosition + 1) % _capacity;
            }
            _samplesWritten = Math.Min(_samplesWritten + frames, _capacity);
        }
    }

    public void ReadLatest(int frameCount, float[] leftOut, float[] rightOut)
    {
        lock (_lock)
        {
            int available = Math.Min(frameCount, _samplesWritten);
            int startPos = (_writePosition - available + _capacity) % _capacity;
            int padding = frameCount - available;

            Array.Clear(leftOut, 0, padding);
            Array.Clear(rightOut, 0, padding);

            for (int i = 0; i < available; i++)
            {
                int pos = (startPos + i) % _capacity;
                leftOut[padding + i] = _left[pos];
                rightOut[padding + i] = _right[pos];
            }
        }
    }
}
