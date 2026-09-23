namespace NeonSidekick.Audio;

/// <summary>
/// Speaker output. Callers push PCM; the implementation feeds it to the device and reports what
/// is still unplayed. The format is fixed by the implementation's constructor.
///
/// <para>The seam exists so the speech path is testable without a device (the test project's
/// <c>FakeAudioPlayback</c>) and so the WinMM P/Invoke lives in exactly one class.</para>
/// </summary>
public interface IAudioPlayback : IDisposable
{
    PcmFormat Format { get; }

    /// <summary>Whether the device is open and accepting audio.</summary>
    bool IsPlaying { get; }

    /// <summary>
    /// Bytes accepted by <see cref="Write"/> that the device has not yet finished playing. Zero
    /// means everything written has been heard (or dropped by <see cref="ClearBuffer"/>).
    /// </summary>
    long BufferedBytes { get; }

    /// <summary>Opens the device. Throws when it cannot. Calling it while playing is a no-op.</summary>
    void Start();

    /// <summary>Queues <paramref name="count"/> bytes of <paramref name="pcm"/>. Safe from any thread; ignored when not started.</summary>
    void Write(byte[] pcm, int count);

    /// <summary>Drops everything queued and silences the device now. The interruption primitive.</summary>
    void ClearBuffer();

    /// <summary>Stops and releases the device. Safe when not running; <see cref="Start"/> works again afterwards.</summary>
    void Stop();
}
