using System.Globalization;
using NeonCompanion.Audio;
using NeonCompanion.Settings;
using NeonCompanion.Speech;
using NeonCompanion.UI;
using Spectre.Console;

namespace NeonCompanion.App;

/// <summary>
/// <c>--voice-check</c>: records a few seconds from the default microphone, transcribes them and
/// prints the result; exit 0 only when words came back. The operator's proof that the whole
/// voice-input path (WinMM capture, Silero, Whisper.net and the model files) works in the
/// <em>published</em> binary, which is the only build where the NativeAOT failures show.
///
/// <para>The saved <c>SttInput</c> switch is ignored (forced on for the check), so it can
/// be run before ever enabling voice input. Models are downloaded on first run, exactly as the
/// chat screen would. Plain lines, no spinner: this mode's console <em>is</em> its interface,
/// like <c>--smoke</c> and <c>--audio-check</c>. Not part of the <c>build.ps1</c> gate — it needs
/// a microphone and someone to talk into it.</para>
/// </summary>
internal static class VoiceCheck
{
    public const int ListenSeconds = 5;

    public const string ListeningLine = "  listening - say something now.";
    public const string NoTranscriptLine = "RESULT: NO TRANSCRIPT. Voice input is not working.";

    /// <summary>The first line printed. Pinned.</summary>
    public static string IntroLine => Invariant($"Voice input check: up to {ListenSeconds} s from the default microphone at {PcmFormat.Whisper}.");

    public static string TranscriptLine(string text, TimeSpan audio, TimeSpan elapsed) =>
        Invariant($"RESULT: heard \"{text}\" ({audio.TotalSeconds:F1} s of audio, transcribed in {elapsed.TotalMilliseconds:F0} ms)");

    public static string HeardNothingLine(ListenEnd endedBy) => $"  heard nothing (ended by {endedBy})";

    /// <summary>Five seconds of no speech, five seconds at most; the watchdog stays at its default.</summary>
    public static readonly VoicePipelineOptions CheckOptions =
        VoicePipelineOptions.Default with { NoSpeechTimeout = TimeSpan.FromSeconds(ListenSeconds), MaxUtterance = TimeSpan.FromSeconds(ListenSeconds) };

    /// <summary>Runs the check over <paramref name="voice"/> (the caller disposes it) and returns the process exit code.</summary>
    public static async Task<int> RunAsync(IAnsiConsole console, VoiceSession voice, AppSettingsData effective, VoicePipelineOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(voice);
        ArgumentNullException.ThrowIfNull(effective);

        var settings = AppSettings.Copy(effective);
        settings.SttInput = true;
        settings.SttWake = false;   // the check proves push-to-talk; no 40 MB Vosk download for it
        settings.SttInterrupt = false;  // same reason: interrupting also needs the Vosk model

        console.MarkupLine(Theme.DimMarkup(IntroLine));

        string? lastPhase = null;
        void Phase(string label)
        {
            // Download progress arrives once per percent; print each label once, without the percentage.
            string stem = label.EndsWith('%') && label.LastIndexOf(' ') is var cut and > 0 ? label[..cut] : label;
            if (stem == lastPhase)
            {
                return;
            }

            lastPhase = stem;
            console.MarkupLine(Theme.DimMarkup("  " + stem));
        }

        await voice.ConnectAsync(settings, Phase, cancellationToken).ConfigureAwait(false);
        if (!voice.IsReady)
        {
            console.MarkupLine(Theme.ColorMarkup(Theme.Bad, "  " + voice.StatusLine()));
            console.MarkupLine(Theme.ColorMarkup(Theme.Bad, NoTranscriptLine));
            return 1;
        }

        console.MarkupLine(Theme.DimMarkup(ListeningLine));
        voice.PipelineOptions = options ?? CheckOptions;

        ListenResult result;
        try
        {
            result = await voice.ListenAsync(CancellationToken.None, Phase, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            console.MarkupLine(Theme.ColorMarkup(Theme.Bad, $"  Voice input THREW {ex.GetType().Name}: {ex.Message}"));
            console.MarkupLine(Theme.ColorMarkup(Theme.Bad, NoTranscriptLine));
            return 1;
        }

        if (!result.Ok)
        {
            console.MarkupLine(Theme.ColorMarkup(Theme.Bad, "  " + result.Detail));
            console.MarkupLine(Theme.ColorMarkup(Theme.Bad, NoTranscriptLine));
            return 1;
        }

        if (result.HeardNothing)
        {
            console.MarkupLine(Theme.DimMarkup(HeardNothingLine(result.EndedBy)));
            console.MarkupLine(Theme.ColorMarkup(Theme.Bad, NoTranscriptLine));
            return 1;
        }

        console.MarkupLine(Theme.ColorMarkup(Theme.Good, TranscriptLine(result.Text, result.Audio, result.Elapsed)));
        return 0;
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
