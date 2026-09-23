using System.Buffers.Binary;
using System.Text;

namespace NeonSidekick.Speech;

/// <summary>
/// The PCM inside what a synthesizer hands back. KokoroSharp's <c>KokoroWavSynthesizer</c> is
/// named for a WAV but its byte output at 0.8.0 is raw 16-bit PCM. The header is stripped only
/// when it is there — <c>RIFF….WAVE</c>, then the chunks walked to
/// <c>data</c> — and anything else is handed through whole. Pure, pinned.
/// </summary>
public static class WavBytes
{
    private const int MinHeader = 12;

    /// <summary>True when <paramref name="bytes"/> begins <c>RIFF</c> … <c>WAVE</c>.</summary>
    public static bool HasRiffHeader(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= MinHeader
        && bytes[..4].SequenceEqual("RIFF"u8)
        && bytes[8..12].SequenceEqual("WAVE"u8);

    /// <summary>
    /// The sample bytes: the <c>data</c> chunk's contents when <paramref name="bytes"/> is a WAV
    /// (its declared length, cut to what is actually there), else <paramref name="bytes"/> itself.
    /// A RIFF header with no <c>data</c> chunk, or one cut short, is also handed through whole
    /// rather than guessed at.
    /// </summary>
    public static ReadOnlyMemory<byte> Payload(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (!HasRiffHeader(bytes))
        {
            return bytes;
        }

        int offset = MinHeader;
        while (offset + 8 <= bytes.Length)
        {
            ReadOnlySpan<byte> id = bytes.AsSpan(offset, 4);
            int length = (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4)));
            offset += 8;
            if (id.SequenceEqual("data"u8))
            {
                return bytes.AsMemory(offset, Math.Min(length, bytes.Length - offset));
            }

            // Chunks are word-aligned: an odd length carries one pad byte.
            offset += length + (length & 1);
        }

        return bytes;
    }

    /// <summary>The 44-byte canonical header ahead of <paramref name="pcm"/>, for tests and fixtures. Never used on the speech path.</summary>
    public static byte[] Wrap(ReadOnlySpan<byte> pcm, int sampleRate, int bitsPerSample, int channels)
    {
        var bytes = new byte[44 + pcm.Length];
        var span = bytes.AsSpan();
        Encoding.ASCII.GetBytes("RIFF", span[..4]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..8], (uint)(36 + pcm.Length));
        Encoding.ASCII.GetBytes("WAVE", span[8..12]);
        Encoding.ASCII.GetBytes("fmt ", span[12..16]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..20], 16);
        BinaryPrimitives.WriteUInt16LittleEndian(span[20..22], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[22..24], (ushort)channels);
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..28], (uint)sampleRate);
        int blockAlign = channels * bitsPerSample / 8;
        BinaryPrimitives.WriteUInt32LittleEndian(span[28..32], (uint)(sampleRate * blockAlign));
        BinaryPrimitives.WriteUInt16LittleEndian(span[32..34], (ushort)blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(span[34..36], (ushort)bitsPerSample);
        Encoding.ASCII.GetBytes("data", span[36..40]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[40..44], (uint)pcm.Length);
        pcm.CopyTo(span[44..]);
        return bytes;
    }
}
