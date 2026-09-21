namespace NeonCompanion.Audio;

/// <summary>Linear PCM sample format. Little-endian, interleaved when stereo.</summary>
public readonly record struct PcmFormat(int SampleRate, int BitsPerSample, int Channels)
{
    /// <summary>What Kokoro-FastAPI returns for <c>response_format=pcm</c>: 24 kHz, 16-bit, mono.</summary>
    public static PcmFormat Kokoro => new(24000, 16, 1);

    /// <summary>What Whisper and Silero consume: 16 kHz, 16-bit, mono. The capture side records in this format directly.</summary>
    public static PcmFormat Whisper => new(16000, 16, 1);

    /// <summary>Bytes per sample frame (all channels).</summary>
    public int BlockAlign => Channels * BitsPerSample / 8;

    public int BytesPerSecond => SampleRate * BlockAlign;

    /// <summary>Whole frames' worth of bytes for <paramref name="milliseconds"/>.</summary>
    public int BytesFor(int milliseconds) => (int)((long)SampleRate * milliseconds / 1000) * BlockAlign;

    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{SampleRate} Hz, {BitsPerSample}-bit, {(Channels == 1 ? "mono" : Channels == 2 ? "stereo" : Channels + " ch")}");
}
