using NeonSidekick.Audio;

namespace NeonSidekick.Tests.Fakes;

/// <summary>
/// An <see cref="IAudioCapture"/> a test drives by hand. <see cref="Deliver"/> raises
/// <see cref="DataAvailable"/> the way the pump thread would (only while capturing);
/// <see cref="OnStart"/> runs on the thread pool right after <see cref="Start"/> returns, which
/// is where a test feeds buffers or pushes keys, so the outcome does not depend on timing.
/// <see cref="Log"/> records "start" / "stop" (and, via <c>FakeRecognizer</c>,
/// "transcribe") so a test can pin their order.
/// </summary>
public sealed class FakeAudioCapture : IAudioCapture
{
    private readonly object _gate = new();

    public PcmFormat Format { get; init; } = PcmFormat.Whisper;

    public bool ThrowOnStart { get; set; }

    public int Started { get; private set; }

    public int Stopped { get; private set; }

    public bool Disposed { get; private set; }

    public bool IsCapturing { get; private set; }

    /// <summary>Shared with the other fakes of a test so the order of start / stop / transcribe can be asserted.</summary>
    public List<string> Log { get; init; } = new();

    /// <summary>Runs after each <see cref="Start"/>, on the thread pool, with a token that is cancelled by <see cref="Stop"/>.</summary>
    public Func<FakeAudioCapture, CancellationToken, Task>? OnStart { get; set; }

    private CancellationTokenSource? _session;

    public event Action<byte[], int>? DataAvailable;

    public void Start()
    {
        CancellationTokenSource session;
        lock (_gate)
        {
            if (ThrowOnStart)
            {
                throw new InvalidOperationException("waveInOpen failed with MMSYSERR 2.");
            }

            if (IsCapturing)
            {
                return;
            }

            Started++;
            IsCapturing = true;
            Log.Add("start");
            session = _session = new CancellationTokenSource();
        }

        if (OnStart is { } hook)
        {
            _ = Task.Run(() => hook(this, session.Token));
        }
    }

    /// <summary>Raises <see cref="DataAvailable"/> with <paramref name="count"/> bytes of <paramref name="pcm"/>; ignored when not capturing.</summary>
    public void Deliver(byte[] pcm, int count)
    {
        Action<byte[], int>? handler;
        lock (_gate)
        {
            if (!IsCapturing)
            {
                return;
            }

            handler = DataAvailable;
        }

        handler?.Invoke(pcm, count);
    }

    /// <summary><paramref name="milliseconds"/> of silence in <see cref="Format"/>.</summary>
    public byte[] Silence(int milliseconds) => new byte[Format.BytesFor(milliseconds)];

    public void Stop()
    {
        CancellationTokenSource? session;
        lock (_gate)
        {
            if (!IsCapturing)
            {
                return;
            }

            Stopped++;
            IsCapturing = false;
            Log.Add("stop");
            session = _session;
            _session = null;
        }

        session?.Cancel();
        session?.Dispose();
    }

    public void Dispose()
    {
        Stop();
        Disposed = true;
    }
}
