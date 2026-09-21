namespace NeonCompanion.Audio;

/// <summary>
/// Linear-interpolating sample-rate converter for 16-bit mono PCM, streamed: successive calls
/// continue the same signal, so a chunk boundary costs nothing (the last sample of the previous
/// chunk is kept for the interpolation across it). Good enough for a recogniser listening to
/// speech (the interrupt's echo probe feeds Kokoro's 24 kHz output to Vosk at 16 kHz); not for
/// anything anyone listens to. Pure apart from its own carry; pinned by tests.
/// </summary>
internal sealed class LinearResampler
{
    private readonly double _step;
    private double _phase;
    private short _last;
    private bool _hasLast;

    /// <exception cref="ArgumentException">A rate is not positive, or a format is not 16-bit mono.</exception>
    public LinearResampler(PcmFormat from, PcmFormat to)
    {
        if (from.BitsPerSample != 16 || from.Channels != 1 || to.BitsPerSample != 16 || to.Channels != 1)
        {
            throw new ArgumentException("Only 16-bit mono PCM is resampled.");
        }

        if (from.SampleRate <= 0 || to.SampleRate <= 0)
        {
            throw new ArgumentException("Sample rates must be positive.");
        }

        From = from;
        To = to;
        _step = (double)from.SampleRate / to.SampleRate;
    }

    public PcmFormat From { get; }

    public PcmFormat To { get; }

    /// <summary>Whether the rates differ at all; when they do not, <see cref="Resample"/> is a copy.</summary>
    public bool IsIdentity => From.SampleRate == To.SampleRate;

    /// <summary>
    /// Converts the first <paramref name="count"/> bytes of <paramref name="pcm"/> (an odd trailing
    /// byte is dropped). Returns the output samples as bytes; empty when nothing can be produced
    /// yet. The output length is about <c>count × to / from</c>, give or take a sample.
    /// </summary>
    public byte[] Resample(byte[] pcm, int count)
    {
        ArgumentNullException.ThrowIfNull(pcm);
        count = Math.Min(count, pcm.Length);
        int samples = count / 2;
        if (samples <= 0)
        {
            return Array.Empty<byte>();
        }

        if (IsIdentity)
        {
            var copy = new byte[samples * 2];
            Buffer.BlockCopy(pcm, 0, copy, 0, copy.Length);
            return copy;
        }

        // Output positions are measured in input samples; the previous chunk's last sample sits at −1.
        double pos = _hasLast ? _phase : 0;
        int capacity = (int)Math.Ceiling(samples / _step) + 2;
        var output = new byte[capacity * 2];
        int produced = 0;
        while (pos <= samples - 1)
        {
            int index = (int)Math.Floor(pos);
            double fraction = pos - index;
            short a = index < 0 ? _last : Sample(pcm, index);
            short b = Sample(pcm, index + 1 < samples ? index + 1 : index);
            int value = (int)Math.Round(a + (b - a) * fraction);
            short sample = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
            output[produced * 2] = (byte)(sample & 0xFF);
            output[produced * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            produced++;
            pos += _step;
        }

        _phase = pos - samples;
        _last = Sample(pcm, samples - 1);
        _hasLast = true;

        if (produced * 2 == output.Length)
        {
            return output;
        }

        var trimmed = new byte[produced * 2];
        Buffer.BlockCopy(output, 0, trimmed, 0, trimmed.Length);
        return trimmed;
    }

    /// <summary>Forgets the carry, for a new signal.</summary>
    public void Reset()
    {
        _phase = 0;
        _last = 0;
        _hasLast = false;
    }

    private static short Sample(byte[] pcm, int index) => (short)(pcm[index * 2] | (pcm[index * 2 + 1] << 8));
}
