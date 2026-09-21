using System.Buffers.Binary;
using System.Text;
using NeonCompanion.Speech;

namespace NeonCompanion.Tests;

public class WavBytesTests
{
    private static byte[] Pcm(int count) => Enumerable.Range(1, count).Select(i => (byte)i).ToArray();

    [Fact]
    public void Payload_RawPcm_IsHandedThroughWhole()
    {
        var pcm = Pcm(10);
        Assert.False(WavBytes.HasRiffHeader(pcm));
        Assert.Equal(pcm, WavBytes.Payload(pcm).ToArray());
        Assert.Empty(WavBytes.Payload(Array.Empty<byte>()).ToArray());
    }

    [Fact]
    public void Payload_CanonicalHeader_IsStripped()
    {
        var pcm = Pcm(100);
        var wav = WavBytes.Wrap(pcm, 24000, 16, 1);
        Assert.Equal(144, wav.Length);
        Assert.True(WavBytes.HasRiffHeader(wav));
        Assert.Equal(pcm, WavBytes.Payload(wav).ToArray());
    }

    [Fact]
    public void Payload_WalksPastAnExtraChunk_ToTheDataChunk()
    {
        var pcm = Pcm(50);
        var canonical = WavBytes.Wrap(pcm, 24000, 16, 1);
        // RIFF/WAVE + fmt, then an odd-length LIST chunk (one pad byte), then data.
        var list = new byte[8 + 5 + 1];
        Encoding.ASCII.GetBytes("LIST", list.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(list.AsSpan(4, 4), 5);
        var wav = canonical[..36].Concat(list).Concat(canonical[36..]).ToArray();

        Assert.Equal(pcm, WavBytes.Payload(wav).ToArray());
    }

    [Fact]
    public void Payload_ATruncatedOrDatalessHeader_IsHandedThroughWhole()
    {
        var wav = WavBytes.Wrap(Pcm(100), 24000, 16, 1);
        var truncated = wav[..30];   // mid fmt chunk
        Assert.Equal(truncated, WavBytes.Payload(truncated).ToArray());

        var noData = wav[..36];      // RIFF + fmt, the data chunk gone
        Assert.Equal(noData, WavBytes.Payload(noData).ToArray());

        // A data length claiming more than is there is cut to what is there.
        var shortData = wav[..100];
        Assert.Equal(Pcm(56), WavBytes.Payload(shortData).ToArray());
    }
}
