using System.Globalization;
using NeonSidekick.Audio;
using NeonSidekick.UI;
using Spectre.Console;

namespace NeonSidekick.App;

/// <summary>
/// <c>--audio-check</c>: plays a short, quiet tone and reports whether it actually reached the
/// device.
///
/// <para>"The device opened" and "sound came out" are different claims, and the voice path has
/// been caught twice on the gap between them. This exercises the same <see cref="IAudioPlayback"/>
/// the speech queue uses, so a failure here is a failure there. Deliberately not routed through
/// Kokoro: it must work on a machine with no server, and synthesis failing would muddy the answer.
/// Not part of the <c>build.ps1</c> gate — it needs an output device.</para>
///
/// <para>Bytes leaving the queue is the only in-process evidence the device consumed them. It
/// cannot prove the tone was audible — a muted mixer drains just the same — but a queue that does
/// not empty proves it was not.</para>
/// </summary>
public static class AudioCheck
{
    public const int ToneMilliseconds = 500;
    public const double ToneHz = 440;

    /// <summary>Deliberately quiet: a diagnostic that may be run with headphones on.</summary>
    public const short ToneAmplitude = 1500;

    /// <summary>How long after the tone's own length to wait for the device to finish.</summary>
    public static readonly TimeSpan DefaultDrainBudget = TimeSpan.FromMilliseconds(ToneMilliseconds + 1500);

    public const string DrainedLine = "RESULT: audio drained. If you heard nothing, check the output device and volume.";
    public const string NotAllPlayedLine = "RESULT: NOT ALL PLAYED. Speech output is not working.";

    /// <summary>A sine tone as 16-bit little-endian PCM in <paramref name="format"/>. Starts at zero amplitude.</summary>
    public static byte[] Tone(PcmFormat format, int milliseconds, double hz, short amplitude)
    {
        if (format.BitsPerSample != 16)
        {
            throw new ArgumentException("Only 16-bit PCM is generated.", nameof(format));
        }

        int frames = (int)((long)format.SampleRate * milliseconds / 1000);
        var pcm = new byte[frames * format.BlockAlign];
        int offset = 0;
        for (int i = 0; i < frames; i++)
        {
            var value = (short)(Math.Sin(2 * Math.PI * hz * i / format.SampleRate) * amplitude);
            for (int c = 0; c < format.Channels; c++)
            {
                pcm[offset++] = (byte)(value & 0xFF);
                pcm[offset++] = (byte)((value >> 8) & 0xFF);
            }
        }

        return pcm;
    }

    /// <summary>
    /// Runs the check over <paramref name="playback"/> (disposed on return) and returns the
    /// process exit code. This mode's console <em>is</em> its interface, like <c>--smoke</c>.
    /// </summary>
    public static async Task<int> RunAsync(IAnsiConsole console, IAudioPlayback playback, TimeSpan? drainBudget = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(playback);

        var budget = drainBudget ?? DefaultDrainBudget;
        var pcm = Tone(playback.Format, ToneMilliseconds, ToneHz, ToneAmplitude);
        long remaining;

        console.MarkupLine(Theme.DimMarkup(Invariant($"Audio output check: {ToneHz} Hz for {ToneMilliseconds} ms through {playback.Format}.")));

        try
        {
            playback.Start();
            playback.Write(pcm, pcm.Length);
            console.MarkupLine(Theme.DimMarkup("  playing - you should hear it now."));

            var deadline = DateTime.UtcNow + budget;
            while (playback.BufferedBytes > 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }

            remaining = playback.BufferedBytes;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            console.MarkupLine(Theme.ColorMarkup(Theme.Bad, $"  Playback THREW {ex.GetType().Name}: {ex.Message}"));
            playback.Dispose();
            return 1;
        }

        playback.Dispose();

        console.MarkupLine(Theme.DimMarkup(Invariant($"  queued {pcm.Length} bytes, {pcm.Length - remaining} drained, {remaining} left")));

        if (remaining > 0)
        {
            console.MarkupLine(Theme.ColorMarkup(Theme.Bad, NotAllPlayedLine));
            return 1;
        }

        console.MarkupLine(Theme.ColorMarkup(Theme.Good, DrainedLine));
        return 0;
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
