namespace NeonCompanion.App;

/// <summary>
/// A model download's progress as spinner labels (<see cref="VoiceSession.DownloadLabel"/>):
/// one label per whole percent when the server said how big the file is, else one per megabyte.
/// Shared by the voice session (Whisper, Silero, Vosk) and the speech session (Kokoro in-process).
/// </summary>
internal sealed class LabelProgress : IProgress<(long Received, long? Total)>
{
    private readonly Action<string> _phase;
    private readonly string _display;
    private readonly long _approxBytes;
    private int _lastPercent = -1;
    private long _lastMegabyte = -1;

    public LabelProgress(Action<string> phase, string display, long approxBytes)
    {
        _phase = phase;
        _display = display;
        _approxBytes = approxBytes;
    }

    public void Report((long Received, long? Total) value)
    {
        if (value.Total is { } total && total > 0)
        {
            int percent = (int)Math.Min(100, value.Received * 100 / total);
            if (percent == _lastPercent)
            {
                return;
            }

            _lastPercent = percent;
            _phase(VoiceSession.DownloadLabel(_display, total, percent));
            return;
        }

        long megabyte = value.Received / 1_000_000;
        if (megabyte == _lastMegabyte)
        {
            return;
        }

        _lastMegabyte = megabyte;
        _phase(VoiceSession.DownloadLabel(_display, _approxBytes));
    }
}
