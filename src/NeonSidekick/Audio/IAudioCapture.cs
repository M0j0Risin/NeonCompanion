namespace NeonSidekick.Audio;

/// <summary>
/// Microphone input. The implementation opens the device on <see cref="Start"/> and raises
/// <see cref="DataAvailable"/> per captured buffer on its own pump thread until <see cref="Stop"/>.
/// The format is fixed by the implementation's constructor.
///
/// <para>The seam exists so the voice pipeline is testable without a microphone (the test
/// project's <c>FakeAudioCapture</c>) and so the WinMM wave-in P/Invoke lives in exactly one
/// class.</para>
/// </summary>
public interface IAudioCapture : IDisposable
{
    PcmFormat Format { get; }

    /// <summary>Whether the device is open and delivering buffers.</summary>
    bool IsCapturing { get; }

    /// <summary>
    /// Raised per captured buffer, on the capture thread, with the buffer and how many bytes of it
    /// are valid. <b>The array is reused between buffers</b>: copy anything that must outlive the
    /// call. Subscribers must not touch the console.
    /// </summary>
    event Action<byte[], int>? DataAvailable;

    /// <summary>Opens the default device and starts delivering. Throws when the device cannot open. A no-op while capturing.</summary>
    void Start();

    /// <summary>
    /// Stops delivering and releases the device; no <see cref="DataAvailable"/> is raised after it
    /// returns. Safe when not capturing; <see cref="Start"/> works again afterwards.
    /// </summary>
    void Stop();
}
