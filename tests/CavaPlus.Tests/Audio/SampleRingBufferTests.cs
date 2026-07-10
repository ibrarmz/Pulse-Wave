using CavaPlus.Audio;

namespace CavaPlus.Tests.Audio;

public class SampleRingBufferTests
{
    [Fact]
    public void ReadLatest_BeforeAnyWrite_ReturnsZeros()
    {
        var buffer = new SampleRingBuffer(capacityPerChannel: 8);
        var left = new float[4];
        var right = new float[4];

        buffer.ReadLatest(4, left, right);

        Assert.All(left, v => Assert.Equal(0f, v));
        Assert.All(right, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void ReadLatest_AfterPartialWrite_PadsWithZerosAndPreservesOrder()
    {
        var buffer = new SampleRingBuffer(capacityPerChannel: 8);
        buffer.WriteInterleavedStereo(new float[] { 1f, -1f, 2f, -2f }); // 2 frames: (1,-1), (2,-2)
        var left = new float[4];
        var right = new float[4];

        buffer.ReadLatest(4, left, right);

        Assert.Equal(new float[] { 0f, 0f, 1f, 2f }, left);
        Assert.Equal(new float[] { 0f, 0f, -1f, -2f }, right);
    }

    [Fact]
    public void WriteInterleavedStereo_WrapsAroundWhenExceedingCapacity()
    {
        var buffer = new SampleRingBuffer(capacityPerChannel: 4);
        // 6 frames into a 4-frame-capacity buffer -> only the last 4 survive
        buffer.WriteInterleavedStereo(new float[] { 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6 });
        var left = new float[4];
        var right = new float[4];

        buffer.ReadLatest(4, left, right);

        Assert.Equal(new float[] { 3f, 4f, 5f, 6f }, left);
        Assert.Equal(new float[] { 3f, 4f, 5f, 6f }, right);
    }
}
