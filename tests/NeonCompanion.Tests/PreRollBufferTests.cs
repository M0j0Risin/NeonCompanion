using NeonCompanion.Audio;

namespace NeonCompanion.Tests;

public class PreRollBufferTests
{
    private readonly PreRollBuffer _buffer = new(1000, PcmFormat.Whisper);

    [Fact]
    public void Capacity_FollowsTheFormat()
    {
        Assert.Equal(32000, _buffer.Capacity);
        Assert.Equal(1000, _buffer.CaptureMilliseconds);
        Assert.Equal(0, _buffer.ByteCount);
        Assert.Equal(48000, new PreRollBuffer(500, new PcmFormat(24000, 16, 2)).Capacity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Ctor_RejectsANonPositiveDuration(int ms)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PreRollBuffer(ms, PcmFormat.Whisper));
    }

    [Fact]
    public void Write_CountsBytes_AndCopiesFromTheOffset()
    {
        var data = new byte[2000];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i & 0xFF);
        }

        Assert.Equal(1000, _buffer.Write(data, 500, 1000));
        Assert.Equal(1000, _buffer.ByteCount);

        var flushed = _buffer.Flush();
        Assert.Equal(1000, flushed.Length);
        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal((byte)((500 + i) & 0xFF), flushed[i]);
        }
    }

    [Fact]
    public void Write_RejectsBadArguments()
    {
        Assert.Throws<ArgumentNullException>(() => _buffer.Write(null!, 0, 1));
        var data = new byte[100];
        Assert.Throws<ArgumentOutOfRangeException>(() => _buffer.Write(data, -1, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => _buffer.Write(data, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _buffer.Write(data, 90, 20));
    }

    [Fact]
    public void Write_WhenFull_OverwritesTheOldest()
    {
        int capacity = _buffer.Capacity;
        var first = new byte[capacity];
        Array.Fill(first, (byte)0xAA);
        _buffer.Write(first, 0, capacity);
        _buffer.Write(new byte[] { 0xBB }, 0, 1);

        Assert.Equal(capacity, _buffer.ByteCount);
        var flushed = _buffer.Flush();
        Assert.Equal(capacity, flushed.Length);
        Assert.Equal(0xAA, flushed[0]);
        Assert.Equal(0xBB, flushed[capacity - 1]);
    }

    [Fact]
    public void Flush_IsChronological_AndClears()
    {
        for (int c = 0; c < 3; c++)
        {
            var chunk = new byte[1000];
            for (int i = 0; i < chunk.Length; i++)
            {
                chunk[i] = (byte)(c * 100 + i);
            }

            _buffer.Write(chunk, 0, chunk.Length);
        }

        var flushed = _buffer.Flush();
        Assert.Equal(3000, flushed.Length);
        for (int c = 0; c < 3; c++)
        {
            for (int i = 0; i < 1000; i++)
            {
                Assert.Equal((byte)(c * 100 + i), flushed[c * 1000 + i]);
            }
        }

        Assert.Equal(0, _buffer.ByteCount);
        Assert.Empty(_buffer.Flush());
    }

    [Fact]
    public void Flush_AfterWrapAround_StartsAtTheOldestByte()
    {
        int capacity = _buffer.Capacity;
        var data = new byte[capacity + 10];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i & 0xFF);
        }

        _buffer.Write(data, 0, data.Length);

        var flushed = _buffer.Flush();
        Assert.Equal(capacity, flushed.Length);
        Assert.Equal(data[10], flushed[0]);
        Assert.Equal(data[^1], flushed[^1]);
    }

    [Fact]
    public void Reset_AndDiscardOldest()
    {
        _buffer.Write(new byte[5000], 0, 5000);
        _buffer.DiscardOldest(2000);
        Assert.Equal(3000, _buffer.ByteCount);
        _buffer.DiscardOldest(-1);
        Assert.Equal(3000, _buffer.ByteCount);
        _buffer.DiscardOldest(3000);
        Assert.Equal(0, _buffer.ByteCount);

        _buffer.Write(new byte[5000], 0, 5000);
        _buffer.Reset();
        Assert.Equal(0, _buffer.ByteCount);
    }
}
