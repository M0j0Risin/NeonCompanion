using NeonSidekick.Audio;
using NeonSidekick.Speech;

namespace NeonSidekick.Tests.Fakes;

/// <summary>
/// An <see cref="ISpeechSynthesizer"/> that records every request and produces silence.
/// <see cref="OnSynthesize"/> runs before the PCM is produced, so a test can push keys or hold
/// the call open until the turn token is cancelled — the same trick as
/// <see cref="FakeChatClient.BeforeUpdate"/>.
/// </summary>
public sealed class FakeSynthesizer : ISpeechSynthesizer
{
    private readonly object _gate = new();
    private readonly List<(string Text, string Voice, double Speed)> _spoken = new();

    public PcmFormat Format => PcmFormat.Kokoro;

    /// <summary>Whether <see cref="ListVoicesAsync"/> reports a server.</summary>
    public bool Exists { get; set; } = true;

    public List<string> Voices { get; } = new() { "af_heart", "af_bella", "bm_george", "am_eric" };   // the two defaults among them, so a fresh profile connects without a warning

    public string ListDetail { get; set; } = "3 voices";

    public int ListCalls { get; private set; }

    /// <summary>How often the session ran the readiness probe (a connect), apart from the picker's listings.</summary>
    public int PrepareCalls { get; private set; }

    /// <summary>Bytes of silence produced per call; even, so it is sample-aligned.</summary>
    public int PcmBytesPerChunk { get; set; } = 4800;

    /// <summary>The 1-based call number from which every call fails. Default: never.</summary>
    public int FailFromCall { get; set; } = int.MaxValue;

    public string FailureDetail { get; set; } = "HTTP 500 on /v1/audio/speech";

    public Func<string, CancellationToken, Task>? OnSynthesize { get; set; }

    public bool Disposed { get; private set; }

    /// <summary>Every synthesis request, in order.</summary>
    public IReadOnlyList<(string Text, string Voice, double Speed)> Spoken
    {
        get
        {
            lock (_gate)
            {
                return _spoken.ToArray();
            }
        }
    }

    public IReadOnlyList<string> SpokenText => Spoken.Select(s => s.Text).ToArray();

    /// <summary>Runs ahead of the readiness probe (a connect held open until a key, say); null = the probe answers at once.</summary>
    public Func<CancellationToken, Task>? OnPrepare { get; set; }

    public async Task<VoiceListResult> PrepareAsync(CancellationToken cancellationToken)
    {
        PrepareCalls++;
        if (OnPrepare is { } hook)
        {
            await hook(cancellationToken).ConfigureAwait(false);
        }

        return await ListVoicesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<VoiceListResult> ListVoicesAsync(CancellationToken cancellationToken)
    {
        ListCalls++;
        return Task.FromResult(Exists
            ? new VoiceListResult(true, Voices.ToArray(), ListDetail)
            : VoiceListResult.Missing("No connection could be made because the target machine actively refused it"));
    }

    public async Task<SynthesisResult> SynthesizeAsync(string text, string voice, double speed, Action<byte[], int> pcmSink, CancellationToken cancellationToken)
    {
        int call;
        lock (_gate)
        {
            _spoken.Add((text, voice, speed));
            call = _spoken.Count;
        }

        if (OnSynthesize is not null)
        {
            await OnSynthesize(text, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (call >= FailFromCall)
        {
            return SynthesisResult.Failed(FailureDetail);
        }

        if (PcmBytesPerChunk > 0)
        {
            pcmSink(new byte[PcmBytesPerChunk], PcmBytesPerChunk);
        }

        return new SynthesisResult(true, PcmBytesPerChunk, PcmBytesPerChunk + " bytes");
    }

    public void Dispose() => Disposed = true;
}
