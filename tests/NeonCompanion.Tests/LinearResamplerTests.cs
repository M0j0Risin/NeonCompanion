using NeonCompanion.Audio;

namespace NeonCompanion.Tests;

public class LinearResamplerTests
{
    private static byte[] Pcm(params short[] samples)
    {
        var pcm = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            pcm[i * 2] = (byte)(samples[i] & 0xFF);
            pcm[i * 2 + 1] = (byte)((samples[i] >> 8) & 0xFF);
        }

        return pcm;
    }

    private static short[] Samples(byte[] pcm)
    {
        var samples = new short[pcm.Length / 2];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
        }

        return samples;
    }

    [Fact]
    public void Rejects_AnythingButSixteenBitMono()
    {
        Assert.Throws<ArgumentException>(() => new LinearResampler(new PcmFormat(24000, 16, 2), PcmFormat.Whisper));
        Assert.Throws<ArgumentException>(() => new LinearResampler(new PcmFormat(24000, 8, 1), PcmFormat.Whisper));
        Assert.Throws<ArgumentException>(() => new LinearResampler(new PcmFormat(0, 16, 1), PcmFormat.Whisper));
    }

    [Fact]
    public void KokoroToWhisper_ProducesTwoThirdsOfTheSamples()
    {
        var resampler = new LinearResampler(PcmFormat.Kokoro, PcmFormat.Whisper);
        Assert.False(resampler.IsIdentity);

        var output = resampler.Resample(new byte[2400 * 2], 2400 * 2);   // 100 ms at 24 kHz

        Assert.InRange(output.Length / 2, 1599, 1601);                    // 100 ms at 16 kHz, give or take a sample
    }

    [Fact]
    public void AConstantSignal_StaysConstant()
    {
        var resampler = new LinearResampler(PcmFormat.Kokoro, PcmFormat.Whisper);
        var input = Pcm(Enumerable.Repeat((short)1234, 300).ToArray());

        var output = Samples(resampler.Resample(input, input.Length));

        Assert.Equal(200, output.Length);
        Assert.All(output, s => Assert.Equal(1234, s));
    }

    [Fact]
    public void ARamp_IsInterpolated()
    {
        // 0, 3, 6, 9, ... at 24 kHz sampled every 1.5 input samples is 0, 4.5, 9, 13.5, 18 — rounded
        // half-to-even by Math.Round, so 4.5 → 4 and 13.5 → 14.
        var resampler = new LinearResampler(PcmFormat.Kokoro, PcmFormat.Whisper);
        var input = Pcm(Enumerable.Range(0, 30).Select(i => (short)(i * 3)).ToArray());

        var output = Samples(resampler.Resample(input, input.Length));

        Assert.Equal(new short[] { 0, 4, 9, 14, 18 }, output.Take(5).ToArray());
    }

    [Fact]
    public void Chunked_MatchesWhole()
    {
        // The carry across a chunk boundary keeps the output identical to a single call.
        var signal = Enumerable.Range(0, 1000).Select(i => (short)(Math.Sin(i * 0.05) * 10000)).ToArray();
        var whole = Samples(new LinearResampler(PcmFormat.Kokoro, PcmFormat.Whisper).Resample(Pcm(signal), signal.Length * 2));

        var chunked = new LinearResampler(PcmFormat.Kokoro, PcmFormat.Whisper);
        var pieces = new List<short>();
        int offset = 0;
        foreach (int size in new[] { 7, 300, 1, 2, 690 })
        {
            var chunk = Pcm(signal.Skip(offset).Take(size).ToArray());
            pieces.AddRange(Samples(chunked.Resample(chunk, chunk.Length)));
            offset += size;
        }

        Assert.Equal(whole, pieces.ToArray());
    }

    [Fact]
    public void Identity_Copies()
    {
        var resampler = new LinearResampler(PcmFormat.Whisper, PcmFormat.Whisper);
        Assert.True(resampler.IsIdentity);
        var input = Pcm(1, 2, 3);

        var output = resampler.Resample(input, input.Length);

        Assert.Equal(input, output);
        Assert.NotSame(input, output);
    }

    [Fact]
    public void AnOddTrailingByte_IsDropped_AndEmptyInIsEmptyOut()
    {
        var resampler = new LinearResampler(PcmFormat.Kokoro, PcmFormat.Whisper);
        Assert.Empty(resampler.Resample(new byte[1], 1));
        Assert.Empty(resampler.Resample(new byte[10], 0));
        Assert.Throws<ArgumentNullException>(() => resampler.Resample(null!, 0));
    }

    [Fact]
    public void Reset_ForgetsTheCarry()
    {
        var resampler = new LinearResampler(PcmFormat.Kokoro, PcmFormat.Whisper);
        var first = resampler.Resample(Pcm(100, 100, 100, 100), 8);
        resampler.Reset();
        var again = resampler.Resample(Pcm(100, 100, 100, 100), 8);

        Assert.Equal(first, again);
    }
}
