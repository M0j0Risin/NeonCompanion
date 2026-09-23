using System.Runtime.InteropServices;
using NeonSidekick.Diagnostics;
using static NeonSidekick.Audio.WinMmNative;

namespace NeonSidekick.Audio;

/// <summary>
/// Microphone capture over WinMM, written to survive NativeAOT. The mirror image of
/// <see cref="WinMmAudioPlayback"/>.
///
/// <para>NAudio cannot capture in a published binary at all: <c>WaveInEvent</c> stops with
/// <c>WaveHeaderUnprepared calling waveInAddBuffer</c> because its <c>WaveHeader</c> is a
/// <em>class</em> passed by value (the prepared flag lands in a temporary native copy), and
/// <c>WasapiCapture</c> throws <c>InvalidProgramException</c> (<c>[ComImport]</c> activation).
/// Measured on one machine and microphone: JIT 60 buffers in 3 s, NativeAOT 0. With this class,
/// 60 either way.</para>
///
/// <para>So: <b>every header lives in unmanaged memory this class owns and is handed to WinMM as
/// an <see cref="IntPtr"/></b>, read and written through a typed pointer. Nothing is marshalled by
/// value. Only the published build shows the failure, which is why <c>--voice-check</c> exists
/// and why <c>audio:winmm-in</c> in <c>--smoke</c> opens the device for real.</para>
///
/// <para>Four 50 ms buffers are queued; a pump thread waits for the driver's event, copies each
/// finished buffer into a scratch array, raises <see cref="DataAvailable"/> and re-queues the
/// header. <see cref="Stop"/> joins the pump <em>outside</em> the lock (a subscriber that reaches
/// back into this class would otherwise deadlock) and resets the device before unpreparing, so no
/// header is freed while the driver still writes into it.</para>
/// </summary>
public sealed unsafe class WinMmAudioCapture : IAudioCapture
{
    private const string Category = "WinMmCapture";

    /// <summary>
    /// Buffers in flight. Four at 50 ms gives 200 ms of slack before the driver runs out of
    /// somewhere to write, ample for a subscriber that only copies and returns.
    /// </summary>
    private const int BufferCount = 4;

    private readonly int _bufferBytes;
    private readonly object _gate = new();

    private IntPtr _device;
    private IntPtr[] _headers = Array.Empty<IntPtr>();
    private AutoResetEvent? _bufferReady;
    private Thread? _pump;
    private volatile bool _running;
    private bool _disposed;

    public WinMmAudioCapture(PcmFormat format, int bufferMilliseconds = 50)
    {
        if (format.SampleRate <= 0 || format.Channels <= 0 || format.BitsPerSample is not (8 or 16))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "WinMM capture needs a positive rate, 8 or 16 bits and at least one channel.");
        }

        if (bufferMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferMilliseconds));
        }

        Format = format;
        _bufferBytes = Math.Max(format.BlockAlign, format.BytesFor(bufferMilliseconds));
    }

    public PcmFormat Format { get; }

    public bool IsCapturing => _running;

    /// <inheritdoc/>
    public event Action<byte[], int>? DataAvailable;

    /// <summary>How many wave-in devices Windows reports. Zero on a headless CI runner.</summary>
    public static int InputDeviceCount() => (int)waveInGetNumDevs();

    /// <summary>
    /// Opens and immediately closes <c>WAVE_MAPPER</c> for capture at <paramref name="format"/>
    /// without recording anything. The <c>--smoke</c> check: proves the imports bind and the open
    /// path round-trips in the published binary. Returns the MMSYSERR code (0 = opened).
    /// </summary>
    public static int ProbeDefaultDevice(PcmFormat format)
    {
        var wave = WaveFormatEx.Pcm(format);
        int result = waveInOpen(out var handle, WaveMapper, ref wave, IntPtr.Zero, IntPtr.Zero, CallbackNull);
        if (result == MmsyserrNoError && handle != IntPtr.Zero)
        {
            waveInClose(handle);
        }

        return result;
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_running)
            {
                return;
            }

            var wave = WaveFormatEx.Pcm(Format);
            _bufferReady = new AutoResetEvent(false);

            int result = waveInOpen(
                out _device,
                WaveMapper,
                ref wave,
                _bufferReady.SafeWaitHandle.DangerousGetHandle(),
                IntPtr.Zero,
                CallbackEvent);

            if (result != MmsyserrNoError)
            {
                _bufferReady.Dispose();
                _bufferReady = null;
                _device = IntPtr.Zero;
                throw new InvalidOperationException($"waveInOpen failed with MMSYSERR {result}.");
            }

            _headers = new IntPtr[BufferCount];
            for (int i = 0; i < BufferCount; i++)
            {
                var data = Marshal.AllocHGlobal(_bufferBytes);
                var header = (WaveHdr*)Marshal.AllocHGlobal(sizeof(WaveHdr));
                *header = default;
                header->lpData = data;
                header->dwBufferLength = (uint)_bufferBytes;
                _headers[i] = (IntPtr)header;

                waveInPrepareHeader(_device, _headers[i], sizeof(WaveHdr));
                waveInAddBuffer(_device, _headers[i], sizeof(WaveHdr));
            }

            _running = true;
            _pump = new Thread(Pump)
            {
                IsBackground = true,
                Name = "NeonSidekick WinMM capture",
                // Above normal, not highest: a dropped buffer costs 50 ms of audio, and starving
                // the UI thread to avoid that is the wrong trade in an interactive app.
                Priority = ThreadPriority.AboveNormal,
            };
            _pump.Start();

            int startResult = waveInStart(_device);
            if (startResult != MmsyserrNoError)
            {
                _running = false;
                ReleaseDevice();
                throw new InvalidOperationException($"waveInStart failed with MMSYSERR {startResult}.");
            }
        }
    }

    public void Stop()
    {
        Thread? pump;

        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            pump = _pump;
            _pump = null;
        }

        // Joined outside the lock: the pump raises DataAvailable, and a subscriber that reaches
        // back into this class while Stop held the lock would deadlock.
        _bufferReady?.Set();
        pump?.Join(TimeSpan.FromSeconds(2));

        lock (_gate)
        {
            ReleaseDevice();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }

    /// <summary>Resets, unprepares, frees and closes. Caller holds the gate and has stopped the pump.</summary>
    private void ReleaseDevice()
    {
        if (_device == IntPtr.Zero)
        {
            return;
        }

        // Reset first: it returns every queued buffer, so unpreparing cannot race the driver
        // still writing into one.
        waveInReset(_device);

        for (int i = 0; i < _headers.Length; i++)
        {
            if (_headers[i] == IntPtr.Zero)
            {
                continue;
            }

            var header = (WaveHdr*)_headers[i];
            waveInUnprepareHeader(_device, _headers[i], sizeof(WaveHdr));
            if (header->lpData != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(header->lpData);
            }

            Marshal.FreeHGlobal(_headers[i]);
        }

        _headers = Array.Empty<IntPtr>();

        waveInClose(_device);
        _device = IntPtr.Zero;

        _bufferReady?.Dispose();
        _bufferReady = null;
    }

    /// <summary>
    /// Waits for the driver, delivers finished buffers and re-queues them. The wait is bounded
    /// rather than indefinite: <see cref="Stop"/> sets the event, but a device that stops
    /// delivering entirely would otherwise park this thread with no way to notice shutdown.
    /// </summary>
    private void Pump()
    {
        var scratch = new byte[_bufferBytes];

        while (_running)
        {
            try
            {
                _bufferReady?.WaitOne(50);

                for (int i = 0; i < _headers.Length && _running; i++)
                {
                    var header = (WaveHdr*)_headers[i];
                    if (header == null || (header->dwFlags & WhdrDone) == 0)
                    {
                        continue;
                    }

                    int recorded = (int)header->dwBytesRecorded;
                    if (recorded > 0)
                    {
                        int count = Math.Min(recorded, scratch.Length);
                        Marshal.Copy(header->lpData, scratch, 0, count);

                        try
                        {
                            DataAvailable?.Invoke(scratch, count);
                        }
                        catch (Exception ex)
                        {
                            // A subscriber throwing here would otherwise kill capture for the
                            // rest of the session, and the subscriber is the whole voice path.
                            DiagnosticLog.Error(Category, $"An audio subscriber threw: {ex.Message}", ex);
                        }
                    }

                    if (!_running)
                    {
                        break;
                    }

                    waveInUnprepareHeader(_device, _headers[i], sizeof(WaveHdr));
                    header->dwFlags = 0;
                    header->dwBytesRecorded = 0;
                    waveInPrepareHeader(_device, _headers[i], sizeof(WaveHdr));
                    waveInAddBuffer(_device, _headers[i], sizeof(WaveHdr));
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error(Category, $"Capture pump failed: {ex.Message}", ex);
                return;
            }
        }
    }
}
